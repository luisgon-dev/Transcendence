using Transcendence.Service.Core.Services.Auth.Models;

namespace Transcendence.Service.Core.Services.Auth.Interfaces;

public interface IAdminAuditService
{
    Task WriteAsync(AdminAuditWriteRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<AdminAuditEntryDto>> ListRecentAsync(int limit, CancellationToken ct = default);
}
