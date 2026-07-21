using Microsoft.EntityFrameworkCore;
using Transcendence.Data.Models.Auth;
using Transcendence.Data.Repositories.Interfaces;

namespace Transcendence.Data.Repositories.Implementations;

public sealed class UserRiotAccountRepository(TranscendenceContext db) : IUserRiotAccountRepository
{
    public Task<UserRiotAccount?> GetByUserIdAsync(Guid userAccountId, CancellationToken ct = default) =>
        db.UserRiotAccounts
            .Include(link => link.UserAccount)
            .ThenInclude(user => user.Roles)
            .FirstOrDefaultAsync(link => link.UserAccountId == userAccountId, ct);

    public Task<UserRiotAccount?> GetByPuuidAsync(string puuid, CancellationToken ct = default) =>
        db.UserRiotAccounts
            .Include(link => link.UserAccount)
            .ThenInclude(user => user.Roles)
            .FirstOrDefaultAsync(link => link.Puuid == puuid, ct);

    public async Task AddAsync(UserRiotAccount link, CancellationToken ct = default) =>
        await db.UserRiotAccounts.AddAsync(link, ct);

    public void Remove(UserRiotAccount link) => db.UserRiotAccounts.Remove(link);

    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}
