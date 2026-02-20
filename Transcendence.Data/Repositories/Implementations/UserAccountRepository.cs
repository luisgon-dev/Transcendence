using Microsoft.EntityFrameworkCore;
using Transcendence.Data.Models.Auth;
using Transcendence.Data.Repositories.Interfaces;

namespace Transcendence.Data.Repositories.Implementations;

public class UserAccountRepository(TranscendenceContext db) : IUserAccountRepository
{
    public Task<UserAccount?> GetByEmailNormalizedAsync(string emailNormalized, CancellationToken ct = default)
    {
        return db.Set<UserAccount>()
            .FirstOrDefaultAsync(x => x.EmailNormalized == emailNormalized, ct);
    }

    public Task<UserAccount?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return db.Set<UserAccount>()
            .FirstOrDefaultAsync(x => x.Id == id, ct);
    }

    public async Task AddUserAsync(UserAccount user, CancellationToken ct = default)
    {
        await db.Set<UserAccount>().AddAsync(user, ct);
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
