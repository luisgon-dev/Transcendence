using Microsoft.EntityFrameworkCore;
using Transcendence.Data.Models.Auth;
using Transcendence.Data.Repositories.Interfaces;

namespace Transcendence.Data.Repositories.Implementations;

public class UserAccountRepository(TranscendenceContext db) : IUserAccountRepository
{
    public Task<UserAccount?> GetByEmailNormalizedAsync(string emailNormalized, CancellationToken ct = default)
    {
        return db.Set<UserAccount>()
            .Include(x => x.Roles)
            .FirstOrDefaultAsync(x => x.EmailNormalized == emailNormalized, ct);
    }

    public Task<UserAccount?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return db.Set<UserAccount>()
            .Include(x => x.Roles)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
    }

    public async Task<IReadOnlyList<UserAccount>> ListByEmailNormalizedAsync(IEnumerable<string> emailNormalized,
        CancellationToken ct = default)
    {
        var normalized = emailNormalized
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim().ToUpperInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (normalized.Length == 0)
            return [];

        return await db.Set<UserAccount>()
            .Include(x => x.Roles)
            .Where(x => normalized.Contains(x.EmailNormalized))
            .ToListAsync(ct);
    }

    public async Task AddUserAsync(UserAccount user, CancellationToken ct = default)
    {
        await db.Set<UserAccount>().AddAsync(user, ct);
    }

    public async Task AddRoleAsync(UserRole role, CancellationToken ct = default)
    {
        await db.Set<UserRole>().AddAsync(role, ct);
    }

    public Task<bool> HasRoleAsync(Guid userAccountId, string role, CancellationToken ct = default)
    {
        return db.Set<UserRole>()
            .AnyAsync(x => x.UserAccountId == userAccountId && x.Role == role, ct);
    }

    public async Task AddRefreshTokenAsync(UserRefreshToken refreshToken, CancellationToken ct = default)
    {
        await db.Set<UserRefreshToken>().AddAsync(refreshToken, ct);
    }

    public Task<UserRefreshToken?> GetActiveRefreshTokenAsync(string tokenHash, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        return db.Set<UserRefreshToken>()
            .Include(x => x.UserAccount)
            .ThenInclude(x => x.Roles)
            .FirstOrDefaultAsync(x =>
                x.TokenHash == tokenHash &&
                x.RevokedAtUtc == null &&
                x.ExpiresAtUtc > now, ct);
    }

    public Task RevokeRefreshTokenAsync(UserRefreshToken token, string? replacedByTokenHash, CancellationToken ct = default)
    {
        token.RevokedAtUtc = DateTime.UtcNow;
        token.ReplacedByTokenHash = replacedByTokenHash;
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken ct = default)
    {
        return db.SaveChangesAsync(ct);
    }
}
