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
    /// Invalidates all analytics cache entries.
    /// Called when patch changes or significant data updates occur.
    /// </summary>
    Task InvalidateAnalyticsCacheAsync(CancellationToken ct);
}
