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
        CancellationToken ct);

    /// <summary>
    /// Gets top 3 builds for a champion in a role with caching.
    /// Data is cached for 24 hours.
    /// </summary>
    Task<ChampionBuildsResponse> GetBuildsAsync(
        int championId,
        string role,
        string? rankTier,
        CancellationToken ct);

    Task<ChampionProBuildsResponse> GetProBuildsAsync(
        int championId,
        string? region,
        string? role,
        string? patch,
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
        CancellationToken ct);

    /// <summary>
    /// Invalidates all analytics cache entries.
    /// Called when patch changes or significant data updates occur.
    /// </summary>
    Task InvalidateAnalyticsCacheAsync(CancellationToken ct);
}
