using Microsoft.EntityFrameworkCore;
using Transcendence.Data.Models.Service;
using Transcendence.Data.Repositories.Interfaces;
namespace Transcendence.Data.Repositories.Implementations;

public class RefreshLockRepository(TranscendenceContext db) : IRefreshLockRepository
{
    public async Task<bool> TryAcquireAsync(string key, TimeSpan ttl, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var lockedUntil = now.Add(ttl);

        // Try to find existing lock
        var existing = await db.RefreshLocks.FirstOrDefaultAsync(x => x.Key == key, ct);
        if (existing == null)
        {
            // Insert new lock
            var lockRow = new RefreshLock
            {
                Id = Guid.NewGuid(),
                Key = key,
                CreatedAtUtc = now,
                LockedUntilUtc = lockedUntil
            };
            db.RefreshLocks.Add(lockRow);
            try
            {
                await db.SaveChangesAsync(ct);
                return true;
            }
            catch (DbUpdateException)
            {
                // Unique constraint failed due to race; treat as not acquired
                return false;
            }
        }

        // If expired, extend and acquire
        if (existing.LockedUntilUtc <= now)
        {
            existing.LockedUntilUtc = lockedUntil;
            try
            {
                await db.SaveChangesAsync(ct);
                return true;
            }
            catch (DbUpdateConcurrencyException)
            {
                return false;
            }
        }

        // Still locked
        return false;
    }

    public async Task ReleaseAsync(string key, CancellationToken ct = default)
    {
        var existing = await db.RefreshLocks.FirstOrDefaultAsync(x => x.Key == key, ct);
        if (existing != null)
        {
            db.RefreshLocks.Remove(existing);
            await db.SaveChangesAsync(ct);
        }
    }

    public Task<RefreshLock?> GetAsync(string key, CancellationToken ct = default)
    {
        return db.RefreshLocks.FirstOrDefaultAsync(x => x.Key == key, ct);
    }
}
