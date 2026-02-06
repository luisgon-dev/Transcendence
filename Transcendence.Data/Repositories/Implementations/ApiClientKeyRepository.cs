using Microsoft.EntityFrameworkCore;
using Transcendence.Data.Models.Auth;
using Transcendence.Data.Repositories.Interfaces;

namespace Transcendence.Data.Repositories.Implementations;

public class ApiClientKeyRepository(TranscendenceContext db) : IApiClientKeyRepository
{
    public Task<ApiClientKey?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return db.Set<ApiClientKey>().FirstOrDefaultAsync(x => x.Id == id, ct);
    }

    public Task<ApiClientKey?> GetActiveByHashAsync(string keyHash, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        return db.Set<ApiClientKey>()
            .Where(x => x.KeyHash == keyHash)
            .Where(x => !x.IsRevoked)
            .Where(x => x.ExpiresAt == null || x.ExpiresAt > now)
            .FirstOrDefaultAsync(ct);
    }

    public Task<List<ApiClientKey>> ListAsync(CancellationToken ct = default)
    {
        return db.Set<ApiClientKey>()
            .AsNoTracking()
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(ct);
    }

    public async Task AddAsync(ApiClientKey key, CancellationToken ct = default)
    {
        await db.Set<ApiClientKey>().AddAsync(key, ct);
    }

    public async Task<bool> RevokeAsync(Guid id, CancellationToken ct = default)
    {
        var key = await db.Set<ApiClientKey>().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (key == null) return false;

        if (!key.IsRevoked)
        {
            key.IsRevoked = true;
            key.RevokedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }

        return true;
    }

    public Task SaveChangesAsync(CancellationToken ct = default)
    {
        return db.SaveChangesAsync(ct);
    }
}
