using Transcendence.Service.Core.Services.Analytics.Models;

namespace Transcendence.Service.Core.Services.Analytics.Interfaces;

/// <summary>
/// Raw computation service for champion analytics.
/// Performs EF Core aggregation queries without caching.
/// </summary>
public interface IChampionAnalyticsComputeService
{
    /// <summary>
    /// Computes win rates for a champion across roles and rank tiers.
    /// Only returns data for combinations with sufficient sample size (100+ games).
    /// </summary>
    Task<List<ChampionWinRateDto>> ComputeWinRatesAsync(
        int championId,
        ChampionAnalyticsFilter filter,
        string patch,
        CancellationToken ct);

    /// <summary>
    /// Computes tier list ranking champions by composite score (70% win rate + 30% pick rate).
    /// Assigns S/A/B/C/D grades by percentile: S=top 10%, A=10-30%, B=30-60%, C=60-85%, D=85%+
    /// </summary>
    Task<List<TierListEntry>> ComputeTierListAsync(
        string? role,
        string? rankTier,
        string? region,
        string patch,
        CancellationToken ct);

    /// <summary>
    /// Win rates served from the precomputed <c>ChampionRoleTierStat</c>/ban aggregate tables (a fast
    /// indexed scope roll-up instead of a raw-match scan), falling back to <see cref="ComputeWinRatesAsync"/>
    /// when no aggregates exist for the patch yet. Produces DTOs identical to the raw path.
    /// </summary>
    Task<List<ChampionWinRateDto>> ComputeWinRatesFromStatsAsync(
        int championId,
        ChampionAnalyticsFilter filter,
        string patch,
        CancellationToken ct);

    /// <summary>
    /// Tier list served from the precomputed aggregate tables, falling back to
    /// <see cref="ComputeTierListAsync"/> when no aggregates exist for the patch yet. Tiering/ordering and
    /// win/pick rates are identical to the raw path; ban rate is role-independent (a deliberate consistency
    /// fix — see <c>ScopeMatchCountStat</c>) so it matches the unified tier list and the win-rate page.
    /// </summary>
    Task<List<TierListEntry>> ComputeTierListFromStatsAsync(
        string? role,
        string? rankTier,
        string? region,
        string patch,
        CancellationToken ct);

    /// <summary>
    /// Computes top 3 builds for a champion with items and runes bundled.
    /// Core items (70%+ appearance) distinguished from situational.
    /// Uses only completed, build-impact items for build quality calculations.
    /// </summary>
    Task<ChampionBuildsResponse> ComputeBuildsAsync(
        int championId,
        string role,
        string? rankTier,
        string? region,
        string patch,
        CancellationToken ct);

    /// <summary>
    /// Builds served from the durable <c>ChampionBuildSnapshot</c> (the persisted live response for the
    /// common all-region scopes), falling back to <see cref="ComputeBuildsAsync"/> for a specific tier/region
    /// or a patch without a snapshot yet. The stored value is the live compute's own output, so it's identical.
    /// </summary>
    Task<ChampionBuildsResponse> ComputeBuildsFromStatsAsync(
        int championId,
        string role,
        string? rankTier,
        string? region,
        string patch,
        CancellationToken ct);

    Task<ChampionProBuildsResponse> ComputeProBuildsAsync(
        int championId,
        string? region,
        string? role,
        string scope,
        string patch,
        CancellationToken ct);

    /// <summary>
    /// Computes champions ranked by pick/play frequency among tracked pro / high-elo players.
    /// Scope selects the roster: "pro" (IsPro), "highelo" (IsHighEloOtp), or "all" (either).
    /// </summary>
    Task<ProChampionPlayrateResponse> ComputeProChampionPlayrateAsync(
        string? region,
        string scope,
        string patch,
        CancellationToken ct);

    /// <summary>
    /// Returns the public roster of tracked pro players (IsActive &amp;&amp; IsPro).
    /// </summary>
    Task<List<ProPlayerDto>> ComputeProRosterAsync(
        string? region,
        CancellationToken ct);

    /// <summary>
    /// Computes matchup data (counters and favorable matchups) for a champion.
    /// Matchups are lane-specific (Mid vs Mid, Top vs Top, etc.).
    /// </summary>
    Task<ChampionMatchupsResponse> ComputeMatchupsAsync(
        int championId,
        string role,
        string? rankTier,
        string? region,
        string patch,
        CancellationToken ct);

    /// <summary>
    /// Matchups served from the precomputed <c>ChampionMatchupStat</c> aggregates (all-region scope), rolled
    /// up to the requested rank scope. Falls back to <see cref="ComputeMatchupsAsync"/> for a specific region
    /// (only the all-region scope is precomputed) or a patch without aggregates yet. Identical DTOs otherwise.
    /// </summary>
    Task<ChampionMatchupsResponse> ComputeMatchupsFromStatsAsync(
        int championId,
        string role,
        string? rankTier,
        string? region,
        string patch,
        CancellationToken ct);
}
