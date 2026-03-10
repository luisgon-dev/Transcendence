using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Transcendence.Data.Models.Service;
using Transcendence.Data.Repositories.Interfaces;

namespace Transcendence.Data.Repositories.Implementations;

public class RefreshLockRepository(TranscendenceContext db, ILogger<RefreshLockRepository>? logger = null)
    : IRefreshLockRepository
{
    public async Task<bool> TryAcquireAsync(string key, TimeSpan ttl, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Lock key is required.", nameof(key));

        var now = DateTime.UtcNow;
        var lockedUntil = now.Add(ttl);
        var rowId = Guid.NewGuid();

        // Acquire lock with an atomic upsert. We only update an existing row when its lease is expired.
        var affectedRows = await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO ""RefreshLocks"" (""Id"", ""Key"", ""CreatedAtUtc"", ""LockedUntilUtc"")
            VALUES ({rowId}, {key}, {now}, {lockedUntil})
            ON CONFLICT (""Key"") DO UPDATE
            SET ""LockedUntilUtc"" = EXCLUDED.""LockedUntilUtc""
            WHERE ""RefreshLocks"".""LockedUntilUtc"" <= {now};", ct);

        var acquired = affectedRows > 0;
        EmitLifecycleLog(
            "refresh_lock.lifecycle",
            key,
            acquired ? "acquired" : "contention",
            ttlSeconds: ttl.TotalSeconds);

        return acquired;
    }

    public async Task ReleaseAsync(string key, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Lock key is required.", nameof(key));

        var now = DateTime.UtcNow;
        try
        {
            var affectedRows = await db.RefreshLocks
                .Where(x => x.Key == key && x.LockedUntilUtc > now)
                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.LockedUntilUtc, now), ct);

            EmitLifecycleLog(
                "refresh_lock.lifecycle",
                key,
                affectedRows > 0 ? "released" : "release_noop");
        }
        catch (Exception ex)
        {
            EmitLifecycleLog("refresh_lock.lifecycle", key, "release_error");
            logger?.LogError(
                ex,
                "event=refresh_lock.lifecycle lock_key={LockKey} outcome=release_error",
                key);
            throw;
        }
    }

    public Task<RefreshLock?> GetAsync(string key, CancellationToken ct = default)
    {
        return db.RefreshLocks
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Key == key, ct);
    }

    public Task<bool> AnyActiveByPrefixAsync(string prefix, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(prefix))
            throw new ArgumentException("Prefix is required.", nameof(prefix));

        var now = DateTime.UtcNow;
        return db.RefreshLocks
            .AsNoTracking()
            .AnyAsync(x => x.Key.StartsWith(prefix) && x.LockedUntilUtc > now, ct);
    }

    public async Task<int> DeleteExpiredAsync(DateTime expiresOnOrBeforeUtc, int batchSize, CancellationToken ct = default)
    {
        if (batchSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(batchSize), "Batch size must be greater than zero.");

        var startedUtc = DateTime.UtcNow;
        var expiredIds = await db.RefreshLocks
            .AsNoTracking()
            .Where(x => x.LockedUntilUtc <= expiresOnOrBeforeUtc)
            .OrderBy(x => x.LockedUntilUtc)
            .Select(x => x.Id)
            .Take(batchSize)
            .ToListAsync(ct);

        if (expiredIds.Count == 0)
        {
            EmitCleanupLog(
                deletedCount: 0,
                batchSize,
                expiresOnOrBeforeUtc,
                DateTime.UtcNow - startedUtc);
            return 0;
        }

        var deleted = await db.RefreshLocks
            .Where(x => expiredIds.Contains(x.Id))
            .ExecuteDeleteAsync(ct);

        EmitCleanupLog(
            deleted,
            batchSize,
            expiresOnOrBeforeUtc,
            DateTime.UtcNow - startedUtc);
        return deleted;
    }

    public async Task<RefreshLockGrowthSnapshot> GetGrowthSnapshotAsync(DateTime nowUtc, CancellationToken ct = default)
    {
        var activeCount = await db.RefreshLocks
            .AsNoTracking()
            .CountAsync(x => x.LockedUntilUtc > nowUtc, ct);

        var expiredCount = await db.RefreshLocks
            .AsNoTracking()
            .CountAsync(x => x.LockedUntilUtc <= nowUtc, ct);

        var snapshot = new RefreshLockGrowthSnapshot(
            ActiveCount: activeCount,
            ExpiredCount: expiredCount
        );

        try
        {
            logger?.LogDebug(
                "event=refresh_lock.growth_snapshot lock_class={LockClass} platform_region={PlatformRegion} outcome={Outcome} active_count={ActiveCount} expired_count={ExpiredCount}",
                "refresh-lock-lifecycle",
                "GLOBAL",
                "snapshot",
                snapshot.ActiveCount,
                snapshot.ExpiredCount);
        }
        catch
        {
            // best effort telemetry only
        }

        return snapshot;
    }

    private void EmitCleanupLog(int deletedCount, int batchSize, DateTime cutoffUtc, TimeSpan duration)
    {
        try
        {
            logger?.LogDebug(
                "event=refresh_lock.cleanup lock_class={LockClass} platform_region={PlatformRegion} outcome={Outcome} deleted={DeletedCount} batch_size={BatchSize} cutoff_utc={CutoffUtc:o} cleanup_duration_ms={CleanupDurationMilliseconds}",
                "refresh-lock-lifecycle",
                "GLOBAL",
                "batch",
                deletedCount,
                batchSize,
                cutoffUtc,
                Math.Max(0, duration.TotalMilliseconds));
        }
        catch
        {
            // best effort telemetry only
        }
    }

    private void EmitLifecycleLog(string eventName, string lockKey, string outcome, double? ttlSeconds = null)
    {
        try
        {
            var dimensions = ResolveDimensions(lockKey);
            logger?.LogDebug(
                "event={EventName} lock_class={LockClass} platform_region={PlatformRegion} outcome={Outcome} lock_key={LockKey} ttl_seconds={TtlSeconds}",
                eventName,
                dimensions.LockClass,
                dimensions.PlatformRegion,
                outcome,
                lockKey,
                ttlSeconds);
        }
        catch
        {
            // best effort telemetry only
        }
    }

    private static LockDimensions ResolveDimensions(string lockKey)
    {
        if (string.IsNullOrWhiteSpace(lockKey))
            return new LockDimensions("unknown", "UNKNOWN");

        var trimmed = lockKey.Trim();
        if (trimmed.StartsWith("summoner-refresh:", StringComparison.OrdinalIgnoreCase))
            return new LockDimensions("summoner-refresh", ExtractPlatform(trimmed["summoner-refresh:".Length..]));

        if (trimmed.StartsWith("refresh-priority:api:", StringComparison.OrdinalIgnoreCase))
            return new LockDimensions("refresh-priority:api", ExtractPlatform(trimmed["refresh-priority:api:".Length..]));

        var parts = trimmed.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var lockClass = parts.Length > 0 ? parts[0].ToLowerInvariant() : "unknown";
        var platformRegion = parts.Length > 1 ? parts[1].ToUpperInvariant() : "UNKNOWN";
        return new LockDimensions(lockClass, platformRegion);
    }

    private static string ExtractPlatform(string suffix)
    {
        var delimiter = suffix.IndexOf(':');
        var platform = delimiter < 0 ? suffix : suffix[..delimiter];
        return string.IsNullOrWhiteSpace(platform) ? "UNKNOWN" : platform.Trim().ToUpperInvariant();
    }

    private readonly record struct LockDimensions(string LockClass, string PlatformRegion);
}
