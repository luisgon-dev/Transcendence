using Microsoft.EntityFrameworkCore;
using Transcendence.Data.Models.Service;
using Transcendence.Data.Repositories.Interfaces;

namespace Transcendence.Data.Repositories.Implementations;

public class RefreshLockRepository(TranscendenceContext db) : IRefreshLockRepository
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

        return affectedRows > 0;
    }

    public async Task ReleaseAsync(string key, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Lock key is required.", nameof(key));

        var now = DateTime.UtcNow;
        await db.RefreshLocks
            .Where(x => x.Key == key && x.LockedUntilUtc > now)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.LockedUntilUtc, now), ct);
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
}
