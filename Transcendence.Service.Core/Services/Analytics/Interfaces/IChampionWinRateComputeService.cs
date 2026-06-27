using Transcendence.Service.Core.Services.Analytics.Models;

namespace Transcendence.Service.Core.Services.Analytics.Interfaces;

/// <summary>
/// Raw computation service for champion win-rate and tier-list analytics (and their stats-backed
/// fast paths). Extracted from the original analytics compute contract (P10.1) so the win-rate / tier-list
/// domain is a focused unit; builds, pro builds/playrate, and matchups
/// (<see cref="IChampionMatchupComputeService"/>) live in their own services. Performs EF Core aggregation
/// without caching.
/// </summary>
public interface IChampionWinRateComputeService
{
    /// <summary>
    /// Computes win rates for a champion across roles and rank tiers.
    /// Only returns data for combinations with sufficient sample size (adaptive minimum).
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
    /// When the precomputed analytics aggregates for <paramref name="patch"/> were last rebuilt (all rows of
    /// a refresh share one timestamp), or null if the patch has no aggregates yet. Surfaced as the
    /// "updated N ago" freshness signal.
    /// </summary>
    Task<DateTime?> GetAnalyticsComputedAtAsync(string patch, CancellationToken ct);
}
