using Transcendence.Service.Core.Services.Tft.Models;

namespace Transcendence.Service.Core.Services.Tft.Interfaces;

public interface ITftStaticDataService
{
    Task UpdateStaticDataAsync(CancellationToken ct = default);
    Task EnsureStaticDataAsync(CancellationToken ct = default);
    Task<int?> GetActiveSetNumberAsync(CancellationToken ct = default);
    Task<IReadOnlyList<TftStaticEntityDto>> GetChampionCatalogAsync(CancellationToken ct = default);
    Task<TftStaticEntityDto?> GetChampionByApiNameAsync(string apiName, CancellationToken ct = default);
    Task<IReadOnlyList<TftStaticEntityDto>> GetItemCatalogAsync(CancellationToken ct = default);
    Task<TftStaticEntityDto?> GetItemByApiNameAsync(string apiName, CancellationToken ct = default);
    Task<IReadOnlyList<TftStaticEntityDto>> GetTraitCatalogAsync(CancellationToken ct = default);
    Task<TftStaticEntityDto?> GetTraitByApiNameAsync(string apiName, CancellationToken ct = default);
    Task<IReadOnlyList<TftStaticEntityDto>> GetAugmentCatalogAsync(CancellationToken ct = default);
    Task<TftStaticEntityDto?> GetAugmentByApiNameAsync(string apiName, CancellationToken ct = default);
}
