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

    public async Task AddPasswordResetTokenAsync(UserPasswordResetToken resetToken, CancellationToken ct = default)
    {
        await db.Set<UserPasswordResetToken>().AddAsync(resetToken, ct);
    }

    public Task<UserPasswordResetToken?> GetActivePasswordResetTokenAsync(
        string tokenHash,
        CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        return db.Set<UserPasswordResetToken>()
            .AsNoTracking()
            .Include(x => x.UserAccount)
            .FirstOrDefaultAsync(x =>
                x.TokenHash == tokenHash &&
                x.UsedAtUtc == null &&
                x.ExpiresAtUtc > now,
                ct);
    }

    public async Task<int> RevokeActivePasswordResetTokensForUserAsync(
        Guid userAccountId,
        CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var tokens = await db.Set<UserPasswordResetToken>()
            .Where(x => x.UserAccountId == userAccountId && x.UsedAtUtc == null)
            .ToListAsync(ct);
        foreach (var token in tokens)
            token.UsedAtUtc = now;
        return tokens.Count;
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

    public Task<UserRefreshToken?> GetRefreshTokenByHashAsync(string tokenHash, CancellationToken ct = default)
    {
        // Any state — revoked/expired included — so the refresh flow can distinguish "never existed"
        // from "already rotated" (reuse). Includes the account + roles so an active hit can mint tokens.
        return db.Set<UserRefreshToken>()
            .AsNoTracking()
            .Include(x => x.UserAccount)
            .ThenInclude(x => x.Roles)
            .FirstOrDefaultAsync(x => x.TokenHash == tokenHash, ct);
    }

    public Task RevokeRefreshTokenAsync(UserRefreshToken token, string? replacedByTokenHash, CancellationToken ct = default)
    {
        token.RevokedAtUtc = DateTime.UtcNow;
        token.ReplacedByTokenHash = replacedByTokenHash;
        return Task.CompletedTask;
    }

    public async Task<bool> TryRotateRefreshTokenAsync(string currentHash, UserRefreshToken replacement,
        string protectedReplacement, DateTime now, CancellationToken ct = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await LockAccountAsync(replacement.UserAccountId, ct);
        var claimed = await db.UserRefreshTokens
            .Where(x => x.TokenHash == currentHash && x.UserAccountId == replacement.UserAccountId
                && x.RevokedAtUtc == null && x.ExpiresAtUtc > now)
            .ExecuteUpdateAsync(set => set
                .SetProperty(x => x.RevokedAtUtc, now)
                .SetProperty(x => x.ReplacedByTokenHash, replacement.TokenHash)
                .SetProperty(x => x.ProtectedReplacementToken, protectedReplacement)
                .SetProperty(x => x.FamilyId, replacement.FamilyId), ct);
        if (claimed != 1) return false;
        db.UserRefreshTokens.Add(replacement);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return true;
    }

    public async Task<int> RevokeRefreshTokenFamilyAsync(Guid userId, Guid familyId, CancellationToken ct = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await LockAccountAsync(userId, ct);
        var count = await db.UserRefreshTokens
            .Where(x => x.UserAccountId == userId && x.RevokedAtUtc == null
                && (x.FamilyId == familyId || (x.FamilyId == Guid.Empty && x.Id == familyId)))
            .ExecuteUpdateAsync(set => set.SetProperty(x => x.RevokedAtUtc, DateTime.UtcNow), ct);
        await transaction.CommitAsync(ct);
        return count;
    }

    public async Task<bool> TryCompletePasswordResetAsync(Guid tokenId, Guid userId, string passwordHash,
        DateTime now, CancellationToken ct = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // Account-level serialization also fences a refresh that races a password reset.
        await LockAccountAsync(userId, ct);
        var consumed = await db.UserPasswordResetTokens
            .Where(x => x.Id == tokenId && x.UserAccountId == userId && x.UsedAtUtc == null && x.ExpiresAtUtc > now)
            .ExecuteUpdateAsync(set => set.SetProperty(x => x.UsedAtUtc, now), ct);
        if (consumed != 1) return false;
        await db.UserAccounts.Where(x => x.Id == userId).ExecuteUpdateAsync(set => set
            .SetProperty(x => x.PasswordHash, passwordHash).SetProperty(x => x.FailedLoginAttempts, 0)
            .SetProperty(x => x.LockoutUntilUtc, (DateTime?)null).SetProperty(x => x.UpdatedAtUtc, now), ct);
        await db.UserPasswordResetTokens.Where(x => x.UserAccountId == userId && x.UsedAtUtc == null)
            .ExecuteUpdateAsync(set => set.SetProperty(x => x.UsedAtUtc, now), ct);
        await db.UserRefreshTokens.Where(x => x.UserAccountId == userId && x.RevokedAtUtc == null)
            .ExecuteUpdateAsync(set => set.SetProperty(x => x.RevokedAtUtc, now), ct);
        await transaction.CommitAsync(ct);
        return true;
    }

    private async Task LockAccountAsync(Guid userId, CancellationToken ct)
    {
        if (db.Database.ProviderName == "Npgsql.EntityFrameworkCore.PostgreSQL")
            _ = await db.Database.SqlQuery<Guid>($"SELECT \"Id\" AS \"Value\" FROM \"UserAccounts\" WHERE \"Id\" = {userId} FOR UPDATE")
                .SingleAsync(ct);
    }

    public async Task<int> RevokeAllActiveRefreshTokensForUserAsync(Guid userAccountId, CancellationToken ct = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await LockAccountAsync(userAccountId, ct);
        var count = await db.Set<UserRefreshToken>()
            .Where(x => x.UserAccountId == userAccountId && x.RevokedAtUtc == null)
            .ExecuteUpdateAsync(set => set.SetProperty(x => x.RevokedAtUtc, DateTime.UtcNow), ct);
        await transaction.CommitAsync(ct);
        return count;
    }

    public async Task<bool> RevokeActiveRefreshTokenByHashAsync(string tokenHash, CancellationToken ct = default)
    {
        var token = await GetRefreshTokenByHashAsync(tokenHash, ct);
        if (token is null || token.ExpiresAtUtc <= DateTime.UtcNow) return false;
        // Logging out an older cookie during rotation also revokes the newly issued successor.
        return await RevokeRefreshTokenFamilyAsync(token.UserAccountId,
            token.FamilyId == Guid.Empty ? token.Id : token.FamilyId, ct) > 0;
    }

    public Task SaveChangesAsync(CancellationToken ct = default)
    {
        return db.SaveChangesAsync(ct);
    }
}
