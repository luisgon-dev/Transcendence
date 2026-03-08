using Transcendence.Service.Core.Services.Analytics.Models;
using Transcendence.Service.Core.Services.Tft.Models;

namespace Transcendence.Service.Core.Services.Tft.Interfaces;

public interface ITftAnalyticsService
{
    Task<IReadOnlyList<AnalyticsRegionDto>> GetRegionsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<TftCompListItemDto>> GetCompListAsync(string? rankTier, string? region, CancellationToken ct = default);
    Task<TftCompDetailDto?> GetCompDetailAsync(string compSlug, string? rankTier, string? region, CancellationToken ct = default);
    Task<IReadOnlyList<TftStaticEntityDto>> GetChampionsAsync(CancellationToken ct = default);
    Task<TftStaticEntityDto?> GetChampionAsync(string championId, CancellationToken ct = default);
    Task<IReadOnlyList<TftStaticEntityDto>> GetItemsAsync(CancellationToken ct = default);
    Task<TftStaticEntityDto?> GetItemAsync(string itemId, CancellationToken ct = default);
    Task<IReadOnlyList<TftStaticEntityDto>> GetTraitsAsync(CancellationToken ct = default);
    Task<TftStaticEntityDto?> GetTraitAsync(string traitId, CancellationToken ct = default);
    Task<IReadOnlyList<TftStaticEntityDto>> GetAugmentsAsync(CancellationToken ct = default);
    Task<TftStaticEntityDto?> GetAugmentAsync(string augmentId, CancellationToken ct = default);
    Task InvalidateCacheAsync(CancellationToken ct = default);
}
