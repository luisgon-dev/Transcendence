using Transcendence.Data.Models.Service;

namespace Transcendence.Data.Repositories.Interfaces;

public interface IRefreshLockRepository
{
    Task<bool> TryAcquireAsync(string key, TimeSpan ttl, CancellationToken ct = default);
    Task ReleaseAsync(string key, CancellationToken ct = default);

    /// <summary>
    /// Acquire like <see cref="TryAcquireAsync"/> but return a per-acquisition fencing token (null when
    /// not acquired). Pair with <see cref="ReleaseOwnedAsync"/> so a stale holder cannot release a lock
    /// that has since been re-acquired by someone else. Used on the acquire→enqueue→job-release handoff.
    /// </summary>
    Task<Guid?> TryAcquireOwnedAsync(string key, TimeSpan ttl, CancellationToken ct = default);

    /// <summary>Release only if <paramref name="ownerToken"/> still matches the current lease.</summary>
    Task ReleaseOwnedAsync(string key, Guid ownerToken, CancellationToken ct = default);
    Task<RefreshLock?> GetAsync(string key, CancellationToken ct = default);
    Task<bool> AnyActiveByPrefixAsync(string prefix, CancellationToken ct = default);
    Task<int> DeleteExpiredAsync(DateTime expiresOnOrBeforeUtc, int batchSize, CancellationToken ct = default);
    Task<RefreshLockGrowthSnapshot> GetGrowthSnapshotAsync(DateTime nowUtc, CancellationToken ct = default);
}

public sealed record RefreshLockGrowthSnapshot(
    int ActiveCount,
    int ExpiredCount
);
