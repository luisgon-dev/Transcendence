using Transcendence.Data.Models.Auth;

namespace Transcendence.Data.Repositories.Interfaces;

public interface IApiClientKeyRepository
{
    Task<ApiClientKey?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<ApiClientKey?> GetActiveByHashAsync(string keyHash, CancellationToken ct = default);
    Task<List<ApiClientKey>> ListAsync(CancellationToken ct = default);
    Task AddAsync(ApiClientKey key, CancellationToken ct = default);
    Task<bool> RevokeAsync(Guid id, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
