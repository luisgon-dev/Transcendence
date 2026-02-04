using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Transcendence.Data;
using Transcendence.Service.Core.Services.Analytics.Interfaces;
using Transcendence.Service.Core.Services.Analytics.Models;

namespace Transcendence.Service.Core.Services.Analytics.Implementations;

/// <summary>
/// Cached analytics service for champion win rates and statistics.
/// Uses HybridCache for 24-hour L2 and 1-hour L1 caching.
/// </summary>
public class ChampionAnalyticsService : IChampionAnalyticsService
{
    private const string WinRateCacheKeyPrefix = "analytics:champion:winrates:";
    private const string AnalyticsCacheTag = "analytics";

    // Analytics cache options: 24hr total, 1hr L1 (analytics computed from large datasets)
    private static readonly HybridCacheEntryOptions AnalyticsCacheOptions = new()
    {
        Expiration = TimeSpan.FromHours(24),
        LocalCacheExpiration = TimeSpan.FromHours(1)
    };

    private readonly TranscendenceContext _context;
    private readonly HybridCache _cache;
    private readonly IChampionAnalyticsComputeService _computeService;

    public ChampionAnalyticsService(
        TranscendenceContext context,
        HybridCache cache,
        IChampionAnalyticsComputeService computeService)
    {
        _context = context;
        _cache = cache;
        _computeService = computeService;
    }

    public async Task<ChampionWinRateSummary> GetWinRatesAsync(
        int championId,
        ChampionAnalyticsFilter filter,
        CancellationToken ct)
    {
        // Get current active patch
        var currentPatch = await _context.Patches
            .AsNoTracking()
            .Where(p => p.IsActive)
            .Select(p => p.Version)
            .FirstOrDefaultAsync(ct);

        if (string.IsNullOrEmpty(currentPatch))
        {
            // No active patch, return empty summary
            return new ChampionWinRateSummary(
                ChampionId: championId,
                Patch: "Unknown",
                ByRoleTier: new List<ChampionWinRateDto>()
            );
        }

        // Build cache key based on filter parameters
        var cacheKey = BuildCacheKey(championId, filter, currentPatch);

        // Get or compute win rates with caching
        var winRates = await _cache.GetOrCreateAsync(
            cacheKey,
            async cancel => await _computeService.ComputeWinRatesAsync(championId, filter, currentPatch, cancel),
            AnalyticsCacheOptions,
            tags: new[] { AnalyticsCacheTag, $"champion:{championId}", $"patch:{currentPatch}" },
            cancellationToken: ct
        );

        return new ChampionWinRateSummary(
            ChampionId: championId,
            Patch: currentPatch,
            ByRoleTier: winRates
        );
    }

    public async Task InvalidateAnalyticsCacheAsync(CancellationToken ct)
    {
        await _cache.RemoveByTagAsync(AnalyticsCacheTag, ct);
    }

    private static string BuildCacheKey(int championId, ChampionAnalyticsFilter filter, string patch)
    {
        var keyParts = new List<string>
        {
            $"{WinRateCacheKeyPrefix}{championId}",
            $"patch:{patch}"
        };

        if (!string.IsNullOrEmpty(filter.RankTier))
            keyParts.Add($"tier:{filter.RankTier}");

        if (!string.IsNullOrEmpty(filter.Region))
            keyParts.Add($"region:{filter.Region}");

        if (!string.IsNullOrEmpty(filter.Role))
            keyParts.Add($"role:{filter.Role}");

        return string.Join(":", keyParts);
    }
}
