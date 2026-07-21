using Microsoft.EntityFrameworkCore;
using Transcendence.Data.Models.Auth;
using Transcendence.Data.Models.LiveGame;
using Transcendence.Data.Repositories.Interfaces;

namespace Transcendence.Data.Repositories.Implementations;

public class UserPreferencesRepository(TranscendenceContext db) : IUserPreferencesRepository
{
    public Task<List<FavoriteSummonerReadModel>> GetFavoritesAsync(
        Guid userAccountId,
        CancellationToken ct = default)
    {
        return db.Set<UserFavoriteSummoner>()
            .AsNoTracking()
            .Where(favorite => favorite.UserAccountId == userAccountId)
            .OrderByDescending(favorite => favorite.CreatedAtUtc)
            .Select(favorite => new FavoriteSummonerReadModel(
                favorite.Id,
                favorite.SummonerPuuid,
                favorite.PlatformRegion,
                favorite.DisplayName,
                favorite.CreatedAtUtc,
                db.Set<LiveGameSnapshot>()
                    .Where(snapshot => snapshot.Puuid == favorite.SummonerPuuid
                                       && snapshot.PlatformRegion == favorite.PlatformRegion)
                    .OrderByDescending(snapshot => snapshot.ObservedAtUtc)
                    .ThenByDescending(snapshot => snapshot.Id)
                    .Select(snapshot => snapshot.State)
                    .FirstOrDefault(),
                db.Set<LiveGameSnapshot>()
                    .Where(snapshot => snapshot.Puuid == favorite.SummonerPuuid
                                       && snapshot.PlatformRegion == favorite.PlatformRegion)
                    .OrderByDescending(snapshot => snapshot.ObservedAtUtc)
                    .ThenByDescending(snapshot => snapshot.Id)
                    .Select(snapshot => snapshot.GameId)
                    .FirstOrDefault(),
                db.Set<LiveGameSnapshot>()
                    .Where(snapshot => snapshot.Puuid == favorite.SummonerPuuid
                                       && snapshot.PlatformRegion == favorite.PlatformRegion)
                    .OrderByDescending(snapshot => snapshot.ObservedAtUtc)
                    .ThenByDescending(snapshot => snapshot.Id)
                    .Select(snapshot => (DateTime?)snapshot.ObservedAtUtc)
                    .FirstOrDefault()))
            .ToListAsync(ct);
    }

    public Task<UserFavoriteSummoner?> GetFavoriteByPuuidAsync(
        Guid userAccountId,
        string puuid,
        string platformRegion,
        CancellationToken ct = default)
    {
        return db.Set<UserFavoriteSummoner>().FirstOrDefaultAsync(x =>
            x.UserAccountId == userAccountId &&
            x.SummonerPuuid == puuid &&
            x.PlatformRegion == platformRegion, ct);
    }

    public async Task AddFavoriteAsync(UserFavoriteSummoner favorite, CancellationToken ct = default)
    {
        await db.Set<UserFavoriteSummoner>().AddAsync(favorite, ct);
    }

    public Task<UserFavoriteSummoner?> GetFavoriteByIdAsync(Guid userAccountId, Guid favoriteId,
        CancellationToken ct = default)
    {
        return db.Set<UserFavoriteSummoner>()
            .FirstOrDefaultAsync(x => x.UserAccountId == userAccountId && x.Id == favoriteId, ct);
    }

    public Task RemoveFavoriteAsync(UserFavoriteSummoner favorite, CancellationToken ct = default)
    {
        db.Set<UserFavoriteSummoner>().Remove(favorite);
        return Task.CompletedTask;
    }

    public Task<UserPreferences?> GetPreferencesAsync(Guid userAccountId, CancellationToken ct = default)
    {
        return db.Set<UserPreferences>()
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.UserAccountId == userAccountId, ct);
    }

    public async Task UpsertPreferencesAsync(UserPreferences preferences, CancellationToken ct = default)
    {
        var existing = await db.Set<UserPreferences>()
            .FirstOrDefaultAsync(x => x.UserAccountId == preferences.UserAccountId, ct);
        if (existing == null)
        {
            await db.Set<UserPreferences>().AddAsync(preferences, ct);
            return;
        }

        existing.PreferredRegion = preferences.PreferredRegion;
        existing.PreferredRankTier = preferences.PreferredRankTier;
        existing.LivePollingEnabled = preferences.LivePollingEnabled;
        existing.UpdatedAtUtc = DateTime.UtcNow;
    }

    public Task SaveChangesAsync(CancellationToken ct = default)
    {
        return db.SaveChangesAsync(ct);
    }
}
