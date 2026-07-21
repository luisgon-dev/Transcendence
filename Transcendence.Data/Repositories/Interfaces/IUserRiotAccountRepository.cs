using Transcendence.Data.Models.Auth;

namespace Transcendence.Data.Repositories.Interfaces;

public interface IUserRiotAccountRepository
{
    Task<UserRiotAccount?> GetByUserIdAsync(Guid userAccountId, CancellationToken ct = default);
    Task<UserRiotAccount?> GetByPuuidAsync(string puuid, CancellationToken ct = default);
    Task AddAsync(UserRiotAccount link, CancellationToken ct = default);
    void Remove(UserRiotAccount link);
    Task SaveChangesAsync(CancellationToken ct = default);
}
