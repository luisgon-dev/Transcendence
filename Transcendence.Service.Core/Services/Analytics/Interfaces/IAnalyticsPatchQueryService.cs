using Transcendence.Service.Core.Services.Analytics.Models;

namespace Transcendence.Service.Core.Services.Analytics.Interfaces;

public interface IAnalyticsPatchQueryService
{
    Task<IReadOnlyList<AnalyticsPatchOptionDto>> GetPatchOptionsAsync(CancellationToken ct = default);

    Task<IReadOnlyList<AnalyticsPatchOptionDto>> GetPatchOptionsAsync(
        string? queueFamily,
        CancellationToken ct = default);

    Task<AnalyticsPatchStatusDto> GetActivePatchStatusAsync(CancellationToken ct = default);
}
