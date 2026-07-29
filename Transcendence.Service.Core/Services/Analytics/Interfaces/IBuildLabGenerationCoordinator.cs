using Transcendence.Service.Core.Services.Analytics.Models;

namespace Transcendence.Service.Core.Services.Analytics.Interfaces;

public interface IBuildLabGenerationCoordinator
{
    Task<Guid?> CreatePendingGenerationAsync(CancellationToken ct = default);
    Task<bool> PromoteCandidateAsync(Guid generationId, string? actor = null, CancellationToken ct = default);
    Task<int> PromoteReadyCandidatesAsync(CancellationToken ct = default);
    Task<bool> RollbackAsync(Guid generationId, string? actor = null, CancellationToken ct = default);
    Task<bool> FailGenerationAsync(
        Guid generationId,
        string? reason = null,
        string? actor = null,
        CancellationToken ct = default);
    Task<BuildLabGenerationAdminResponse> GetAdminStatusAsync(CancellationToken ct = default);
}
