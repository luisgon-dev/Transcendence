using Transcendence.Data.Models.Service;

namespace Transcendence.Data.Repositories.Interfaces;

public interface IRefreshLockRepository
{
    Task<bool> TryAcquireAsync(string key, TimeSpan ttl, CancellationToken ct = default);
    Task ReleaseAsync(string key, CancellationToken ct = default);
    Task<RefreshLock?> GetAsync(string key, CancellationToken ct = default);
}