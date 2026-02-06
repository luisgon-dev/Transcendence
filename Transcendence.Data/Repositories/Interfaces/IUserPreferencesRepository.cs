using Transcendence.Data.Models.Auth;

namespace Transcendence.Data.Repositories.Interfaces;

public interface IUserPreferencesRepository
{
    Task<List<UserFavoriteSummoner>> GetFavoritesAsync(Guid userAccountId, CancellationToken ct = default);
    Task<UserFavoriteSummoner?> GetFavoriteByPuuidAsync(Guid userAccountId, string puuid, string platformRegion,
        CancellationToken ct = default);
    Task AddFavoriteAsync(UserFavoriteSummoner favorite, CancellationToken ct = default);
    Task<UserFavoriteSummoner?> GetFavoriteByIdAsync(Guid userAccountId, Guid favoriteId, CancellationToken ct = default);
    Task RemoveFavoriteAsync(UserFavoriteSummoner favorite, CancellationToken ct = default);

    Task<UserPreferences?> GetPreferencesAsync(Guid userAccountId, CancellationToken ct = default);
    Task UpsertPreferencesAsync(UserPreferences preferences, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
