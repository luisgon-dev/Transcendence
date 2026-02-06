using Transcendence.Service.Core.Services.Auth.Models;

namespace Transcendence.Service.Core.Services.Auth.Interfaces;

public interface IUserPreferencesService
{
    Task<IReadOnlyList<FavoriteSummonerDto>> GetFavoritesAsync(Guid userId, CancellationToken ct = default);
    Task<FavoriteSummonerDto> AddFavoriteAsync(Guid userId, AddFavoriteRequest request, CancellationToken ct = default);
    Task<bool> RemoveFavoriteAsync(Guid userId, Guid favoriteId, CancellationToken ct = default);
    Task<UserPreferencesDto> GetPreferencesAsync(Guid userId, CancellationToken ct = default);
    Task<UserPreferencesDto> UpdatePreferencesAsync(Guid userId, UpdateUserPreferencesRequest request,
        CancellationToken ct = default);
}
