using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Options;
using Transcendence.Service.Core.Services.Analytics.Models;
using Transcendence.Service.Core.Services.Jobs.Configuration;
using Transcendence.Service.Core.Services.Tft.Interfaces;
using Transcendence.Service.Core.Services.Tft.Models;

namespace Transcendence.Service.Core.Services.Tft.Implementations;

public class TftAnalyticsService(
    HybridCache cache,
    ITftAnalyticsComputeService computeService,
    ITftStaticDataService staticDataService,
    IOptions<MultiRegionIngestionOptions> multiRegionOptions) : ITftAnalyticsService
{
    private static readonly HybridCacheEntryOptions CacheOptions = new()
    {
        Expiration = TimeSpan.FromHours(6),
        LocalCacheExpiration = TimeSpan.FromMinutes(30)
    };

    public Task<IReadOnlyList<AnalyticsRegionDto>> GetRegionsAsync(CancellationToken ct = default)
    {
        return Task.FromResult<IReadOnlyList<AnalyticsRegionDto>>(AnalyticsRegionCatalog.BuildAvailableRegions(multiRegionOptions.Value));
    }

    public async Task<IReadOnlyList<TftCompListItemDto>> GetCompListAsync(string? rankTier, string? region, CancellationToken ct = default)
    {
        // Set token in the key so a TFT set rollover invalidates stale comps immediately instead of
        // serving them until the 6h cache entry expires.
        var setToken = await ResolveSetTokenAsync(ct);
        return await cache.GetOrCreateAsync(
            $"tft:analytics:comps:{setToken}:{NormalizeRankTier(rankTier)}:{NormalizeRegion(region)}",
            async cancel => await computeService.ComputeCompListAsync(rankTier, region, cancel),
            CacheOptions,
            tags: ["tft-analytics", "tft-comps"],
            cancellationToken: ct);
    }

    public async Task<TftCompDetailDto?> GetCompDetailAsync(string compSlug, string? rankTier, string? region, CancellationToken ct = default)
    {
        var setToken = await ResolveSetTokenAsync(ct);
        return await cache.GetOrCreateAsync(
            $"tft:analytics:comp:{setToken}:{compSlug}:{NormalizeRankTier(rankTier)}:{NormalizeRegion(region)}",
            async cancel => await computeService.ComputeCompDetailAsync(compSlug, rankTier, region, cancel),
            CacheOptions,
            tags: ["tft-analytics", "tft-comps"],
            cancellationToken: ct);
    }

    // Active TFT set as a cache-key token. Resolved the same way as the static-data catalogs
    // (ITftStaticDataService.GetActiveSetNumberAsync); falls back to "set?" when none is active yet.
    private async Task<string> ResolveSetTokenAsync(CancellationToken ct)
    {
        var activeSet = await staticDataService.GetActiveSetNumberAsync(ct);
        return activeSet is { } set ? $"set{set}" : "set?";
    }

    public Task<IReadOnlyList<TftStaticEntityDto>> GetChampionsAsync(CancellationToken ct = default) => staticDataService.GetChampionCatalogAsync(ct);
    public Task<TftStaticEntityDto?> GetChampionAsync(string championId, CancellationToken ct = default) => staticDataService.GetChampionByApiNameAsync(championId, ct);
    public Task<IReadOnlyList<TftStaticEntityDto>> GetItemsAsync(CancellationToken ct = default) => staticDataService.GetItemCatalogAsync(ct);
    public Task<TftStaticEntityDto?> GetItemAsync(string itemId, CancellationToken ct = default) => staticDataService.GetItemByApiNameAsync(itemId, ct);
    public Task<IReadOnlyList<TftStaticEntityDto>> GetTraitsAsync(CancellationToken ct = default) => staticDataService.GetTraitCatalogAsync(ct);
    public Task<TftStaticEntityDto?> GetTraitAsync(string traitId, CancellationToken ct = default) => staticDataService.GetTraitByApiNameAsync(traitId, ct);
    public Task<IReadOnlyList<TftStaticEntityDto>> GetAugmentsAsync(CancellationToken ct = default) => staticDataService.GetAugmentCatalogAsync(ct);
    public Task<TftStaticEntityDto?> GetAugmentAsync(string augmentId, CancellationToken ct = default) => staticDataService.GetAugmentByApiNameAsync(augmentId, ct);

    public async Task InvalidateCacheAsync(CancellationToken ct = default)
    {
        await cache.RemoveByTagAsync("tft-analytics", ct);
    }

    private static string NormalizeRegion(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "ALL" : value.Trim().ToUpperInvariant();
    }

    private static string NormalizeRankTier(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "EMERALD_PLUS";

        var normalized = value.Trim().ToUpperInvariant();
        return normalized == "ALL" ? "all" : normalized;
    }
}
