using Camille.Enums;
using Camille.RiotGames;
using Camille.RiotGames.Util;
using Transcendence.Data.Models.Auth;
using Transcendence.Data.Repositories.Interfaces;
using Transcendence.Service.Core.Services.Auth.Interfaces;
using Transcendence.Service.Core.Services.Auth.Models;
using Transcendence.Service.Core.Services.RiotApi;

namespace Transcendence.Service.Core.Services.Auth.Implementations;

public class UserPreferencesService(
    IUserPreferencesRepository userPreferencesRepository,
    ISummonerRepository summonerRepository,
    RiotGamesApi riotApi) : IUserPreferencesService
{
    public async Task<IReadOnlyList<FavoriteSummonerDto>> GetFavoritesAsync(Guid userId, CancellationToken ct = default)
    {
        var favorites = await userPreferencesRepository.GetFavoritesAsync(userId, ct);
        return favorites.Select(x => new FavoriteSummonerDto(
            x.Id,
            x.SummonerPuuid,
            x.PlatformRegion,
            x.DisplayName,
            x.CreatedAtUtc
        )).ToList();
    }

    public async Task<FavoriteSummonerDto> AddFavoriteAsync(Guid userId, AddFavoriteRequest request, CancellationToken ct = default)
    {
        if (!PlatformRouteParser.TryParse(request.Region, out var platform))
            throw new ArgumentException("Unsupported region.", nameof(request.Region));

        var region = platform.ToString();
        var gameName = request.GameName.Trim();
        var tagLine = request.TagLine.Trim();

        var existingSummoner = await summonerRepository.FindByRiotIdAsync(region, gameName, tagLine, cancellationToken: ct);
        var puuid = existingSummoner?.Puuid;
        if (string.IsNullOrWhiteSpace(puuid))
        {
            try
            {
                var account = await riotApi.AccountV1().GetByRiotIdAsync(platform.ToRegional(), gameName, tagLine, ct);
                puuid = account?.Puuid;
            }
            catch (RiotResponseException ex) when (ex.GetResponse()?.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                throw new ArgumentException("Summoner not found for the provided Riot ID.", nameof(request));
            }
        }

        if (string.IsNullOrWhiteSpace(puuid))
        {
            throw new ArgumentException("Summoner not found for the provided Riot ID.", nameof(request));
        }

        var duplicate = await userPreferencesRepository.GetFavoriteByPuuidAsync(userId, puuid!, region, ct);
        if (duplicate != null)
        {
            return new FavoriteSummonerDto(
                duplicate.Id,
                duplicate.SummonerPuuid,
                duplicate.PlatformRegion,
                duplicate.DisplayName,
                duplicate.CreatedAtUtc
            );
        }

        var favorite = new UserFavoriteSummoner
        {
            Id = Guid.NewGuid(),
            UserAccountId = userId,
            SummonerPuuid = puuid!,
            PlatformRegion = region,
            DisplayName = $"{gameName}#{tagLine}",
            CreatedAtUtc = DateTime.UtcNow
        };

        await userPreferencesRepository.AddFavoriteAsync(favorite, ct);
        await userPreferencesRepository.SaveChangesAsync(ct);

        return new FavoriteSummonerDto(
            favorite.Id,
            favorite.SummonerPuuid,
            favorite.PlatformRegion,
            favorite.DisplayName,
            favorite.CreatedAtUtc
        );
    }

    public async Task<bool> RemoveFavoriteAsync(Guid userId, Guid favoriteId, CancellationToken ct = default)
    {
        var favorite = await userPreferencesRepository.GetFavoriteByIdAsync(userId, favoriteId, ct);
        if (favorite == null) return false;

        await userPreferencesRepository.RemoveFavoriteAsync(favorite, ct);
        await userPreferencesRepository.SaveChangesAsync(ct);
        return true;
    }

    public async Task<UserPreferencesDto> GetPreferencesAsync(Guid userId, CancellationToken ct = default)
    {
        var preferences = await userPreferencesRepository.GetPreferencesAsync(userId, ct);
        if (preferences == null)
        {
            return new UserPreferencesDto(
                PreferredRegion: null,
                PreferredRankTier: null,
                LivePollingEnabled: true,
                UpdatedAtUtc: DateTime.UtcNow
            );
        }

        return new UserPreferencesDto(
            preferences.PreferredRegion,
            preferences.PreferredRankTier,
            preferences.LivePollingEnabled,
            preferences.UpdatedAtUtc
        );
    }

    public async Task<UserPreferencesDto> UpdatePreferencesAsync(Guid userId, UpdateUserPreferencesRequest request,
        CancellationToken ct = default)
    {
        var entity = new UserPreferences
        {
            UserAccountId = userId,
            PreferredRegion = request.PreferredRegion?.Trim(),
            PreferredRankTier = request.PreferredRankTier?.Trim(),
            LivePollingEnabled = request.LivePollingEnabled,
            UpdatedAtUtc = DateTime.UtcNow
        };

        await userPreferencesRepository.UpsertPreferencesAsync(entity, ct);
        await userPreferencesRepository.SaveChangesAsync(ct);

        return new UserPreferencesDto(
            entity.PreferredRegion,
            entity.PreferredRankTier,
            entity.LivePollingEnabled,
            entity.UpdatedAtUtc
        );
    }

}
