using Transcendence.Service.Core.Services.Analytics.Models;

namespace Transcendence.Service.Core.Services.Analytics.Interfaces;

/// <summary>
/// Raw computation service for champion analytics.
/// Performs EF Core aggregation queries without caching.
/// </summary>
public interface IChampionAnalyticsComputeService
{
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
    /// Pro builds served from the durable <c>AnalyticsResponseSnapshot</c> (the persisted live response),
    /// falling back to <see cref="ComputeProBuildsAsync"/> for a specific region, an all-roles request, or a
    /// patch without a snapshot. The stored value is the live compute's own output, so it's identical.
    /// </summary>
    Task<ChampionProBuildsResponse> ComputeProBuildsFromStatsAsync(
        int championId,
        string? region,
        string? role,
        string scope,
        string patch,
        CancellationToken ct);

    /// <summary>
    /// Pro champion playrate served from the durable snapshot, falling back to
    /// <see cref="ComputeProChampionPlayrateAsync"/> for a specific region or a patch without a snapshot.
    /// </summary>
    Task<ProChampionPlayrateResponse> ComputeProChampionPlayrateFromStatsAsync(
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
