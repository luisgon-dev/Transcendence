using Transcendence.Data.Models.Service;

namespace Transcendence.Data.Repositories.Interfaces;

public interface IRefreshLockRepository
{
    Task<bool> TryAcquireAsync(string key, TimeSpan ttl, CancellationToken ct = default);
    Task ReleaseAsync(string key, CancellationToken ct = default);
    Task<RefreshLock?> GetAsync(string key, CancellationToken ct = default);
    Task<bool> AnyActiveByPrefixAsync(string prefix, CancellationToken ct = default);
    Task<int> DeleteExpiredAsync(DateTime expiresOnOrBeforeUtc, int batchSize, CancellationToken ct = default);
    Task<RefreshLockGrowthSnapshot> GetGrowthSnapshotAsync(DateTime nowUtc, CancellationToken ct = default);
}

public sealed record RefreshLockGrowthSnapshot(
    int ActiveCount,
    int ExpiredCount
);
