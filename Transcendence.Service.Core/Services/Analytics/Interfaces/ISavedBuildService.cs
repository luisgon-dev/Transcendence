using Transcendence.Service.Core.Services.Analytics.Models;

namespace Transcendence.Service.Core.Services.Analytics.Interfaces;

public interface ISavedBuildService
{
    Task<SavedBuildListDto> ListAsync(
        Guid userId,
        int? page = null,
        int? pageSize = null,
        CancellationToken ct = default);
    Task<SavedBuildDto> CreateAsync(Guid userId, SaveBuildRequest request, CancellationToken ct = default);
    Task<SavedBuildDto?> UpdateAsync(
        Guid userId,
        Guid savedBuildId,
        SaveBuildRequest request,
        CancellationToken ct = default);
    Task<SavedBuildDto?> RepairAsync(
        Guid userId,
        Guid savedBuildId,
        SavedBuildRepairRequest request,
        CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid userId, Guid savedBuildId, CancellationToken ct = default);
    Task<SavedBuildShareDto?> ShareAsync(Guid userId, Guid savedBuildId, CancellationToken ct = default);
    Task<bool> RevokeShareAsync(Guid userId, Guid savedBuildId, CancellationToken ct = default);
    Task<SavedBuildDto?> GetSharedAsync(Guid shareId, CancellationToken ct = default);
}
