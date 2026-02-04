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
}
