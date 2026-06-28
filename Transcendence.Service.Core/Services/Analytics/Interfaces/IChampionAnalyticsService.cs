using Transcendence.Service.Core.Services.Analytics.Models;

namespace Transcendence.Service.Core.Services.Analytics.Interfaces;

/// <summary>
/// Cached analytics service for champion win rates and statistics.
/// Uses HybridCache for 24-hour L2 and 1-hour L1 caching.
/// </summary>
public interface IChampionAnalyticsService
{
    /// <summary>
    /// Gets champion win rates by role and tier with caching.
    /// Data is cached for 24 hours.
    /// </summary>
    Task<ChampionWinRateSummary> GetWinRatesAsync(
        int championId,
        ChampionAnalyticsFilter filter,
        CancellationToken ct);

    /// <summary>
    /// Gets champion tier list ranked by composite score (70% win rate + 30% pick rate).
    /// Returns S/A/B/C/D tiers with movement indicators from previous patch.
    /// </summary>
    Task<TierListResponse> GetTierListAsync(
        string? role,
        string? rankTier,
        string? region,
        string? patch,
        CancellationToken ct);

    /// <summary>
    /// Gets the champion's tier grade for a specific role (and rank/region/patch scope) — the same grade the
    /// tier list shows. Reads the cached per-role tier list and projects this champion's entry, so the detail
    /// page hero stays consistent with the list. Returns null when the champion is not graded in that scope.
    /// </summary>
    Task<ChampionGradeDto?> GetGradeAsync(
        int championId,
        string role,
        string? rankTier,
        string? region,
        string? patch,
        CancellationToken ct);

    /// <summary>
    /// Gets top 3 builds for a champion in a role with caching.
    /// Data is cached for 24 hours.
    /// </summary>
    Task<ChampionBuildsResponse> GetBuildsAsync(
        int championId,
        string role,
        string? rankTier,
        string? region,
        string? patch,
        CancellationToken ct);

    Task<ChampionProBuildsResponse> GetProBuildsAsync(
        int championId,
        string? region,
        string? role,
        string? scope,
        string? patch,
        CancellationToken ct);

    /// <summary>
    /// Gets champions ranked by pro / high-elo pick frequency, with caching.
    /// Scope: "pro" | "highelo" | "all" (default all). Cached for 24 hours.
    /// </summary>
    Task<ProChampionPlayrateResponse> GetProChampionPlayrateAsync(
        string? region,
        string? scope,
        string? patch,
        CancellationToken ct);

    /// <summary>
    /// Gets the public roster of tracked pro players, with caching.
    /// </summary>
    Task<ProRosterResponse> GetProRosterAsync(
        string? region,
        CancellationToken ct);

    /// <summary>
    /// Gets matchup data (counters and favorable matchups) for a champion.
    /// Matchups are lane-specific (Mid vs Mid, Top vs Top, etc.).
    /// Data is cached for 24 hours.
    /// </summary>
    Task<ChampionMatchupsResponse> GetMatchupsAsync(
        int championId,
        string role,
        string? rankTier,
        string? region,
        string? patch,
        CancellationToken ct);

    /// <summary>
    /// Invalidates all analytics cache entries.
    /// Called when patch changes or significant data updates occur.
    /// </summary>
    Task InvalidateAnalyticsCacheAsync(CancellationToken ct);

    /// <summary>
    /// Invalidates only the analytics cache entries tagged for a single patch.
    /// Used by the routine refresh job so re-ingesting current-patch matches does not
    /// cold-start every cached entry (other patches, pro roster, playrate) at once.
    /// </summary>
    Task InvalidateAnalyticsCacheForPatchAsync(string patch, CancellationToken ct);

    /// <summary>
    /// Recomputes and OVERWRITES (gap-free, via HybridCache SetAsync) the cached analytics the
    /// default champion profile page reads: win rates (at <paramref name="rankTier"/>, region=ALL,
    /// no role), then builds + matchups for the resolved most-played lane, and optionally the
    /// lane-scoped pro-builds default. Lets the hourly warm job keep the default page a permanent
    /// cache hit with ≤refresh-interval-old stats and no invalidate-then-cold window.
    /// Returns the resolved lane, or null when there is no active patch.
    /// </summary>
    Task<string?> RefreshDefaultProfileCacheAsync(
        int championId,
        string? rankTier,
        bool includeProBuilds,
        CancellationToken ct);
}
