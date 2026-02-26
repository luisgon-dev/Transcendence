using Transcendence.Data.Models.Auth;

namespace Transcendence.Data.Repositories.Interfaces;

public interface IUserAccountRepository
{
    Task<UserAccount?> GetByEmailNormalizedAsync(string emailNormalized, CancellationToken ct = default);
    Task<UserAccount?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<UserAccount>> ListByEmailNormalizedAsync(IEnumerable<string> emailNormalized,
        CancellationToken ct = default);
    Task AddUserAsync(UserAccount user, CancellationToken ct = default);
    Task AddRoleAsync(UserRole role, CancellationToken ct = default);
    Task<bool> HasRoleAsync(Guid userAccountId, string role, CancellationToken ct = default);
    Task AddRefreshTokenAsync(UserRefreshToken refreshToken, CancellationToken ct = default);
    Task<UserRefreshToken?> GetActiveRefreshTokenAsync(string tokenHash, CancellationToken ct = default);
    Task RevokeRefreshTokenAsync(UserRefreshToken token, string? replacedByTokenHash, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
