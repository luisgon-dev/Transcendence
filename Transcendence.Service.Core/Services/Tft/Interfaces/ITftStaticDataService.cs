using Transcendence.Service.Core.Services.Tft.Models;

namespace Transcendence.Service.Core.Services.Tft.Interfaces;

public interface ITftStaticDataService
{
    Task UpdateStaticDataAsync(CancellationToken ct = default);
    Task EnsureStaticDataAsync(CancellationToken ct = default);
    Task<int?> GetActiveSetNumberAsync(CancellationToken ct = default);
    Task<IReadOnlyList<TftStaticEntityDto>> GetChampionCatalogAsync(CancellationToken ct = default);
    Task<IReadOnlyList<TftStaticEntityDto>> GetItemCatalogAsync(CancellationToken ct = default);
    Task<IReadOnlyList<TftStaticEntityDto>> GetTraitCatalogAsync(CancellationToken ct = default);
    Task<IReadOnlyList<TftStaticEntityDto>> GetAugmentCatalogAsync(CancellationToken ct = default);
}
