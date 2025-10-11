using Camille.Enums;
using Camille.RiotGames;
using Transcendence.Data;
using Transcendence.Data.Repositories.Interfaces;
using Transcendence.Service.Services.Jobs.Interfaces;
using Transcendence.Service.Services.RiotApi.Interfaces;
namespace Transcendence.Service.Services.Jobs;

public class SummonerRefreshJob(
    ISummonerService summonerService,
    ISummonerRepository summonerRepository,
    IMatchRepository matchRepository,
    IMatchService matchService,
    TranscendenceContext db,
    IRefreshLockRepository refreshLockRepository,
    ILogger<SummonerRefreshJob> logger,
    RiotGamesApi riotGamesApi) : ISummonerRefreshJob
{
    public async Task RefreshByRiotId(string gameName, string tagLine, PlatformRoute platformRoute, string lockKey, CancellationToken ct = default)
    {
        try
        {
            // Fetch/update summoner
            var summoner = await summonerService.GetSummonerByRiotIdAsync(gameName, tagLine, platformRoute, ct);
            await summonerRepository.AddOrUpdateSummonerAsync(summoner, ct);
            await db.SaveChangesAsync(ct);

            // Fetch a small window of recent ranked solo match IDs for this summoner
            var regional = platformRoute.ToRegional();
            var matchIds = await riotGamesApi.MatchV5()
                .GetMatchIdsByPUUIDAsync(regional, summoner.Puuid!, 20, null, Queue.SUMMONERS_RIFT_5V5_RANKED_SOLO, null, null, "ranked", ct);

            // Deduplicate against existing stored matches
            var pending = new List<string>(matchIds);
            foreach (var id in matchIds)
            {
                var existing = await matchRepository.GetMatchByIdAsync(id, ct);
                if (existing != null) pending.Remove(id);
            }

            foreach (var matchId in pending)
            {
                try
                {
                    var match = await matchService.GetMatchDetailsAsync(matchId, regional, platformRoute, ct);
                    if (match == null)
                    {
                        logger.LogInformation("[Refresh] Match {MatchId} failed to fetch for {GameName}#{Tag}", matchId, gameName, tagLine);
                        continue;
                    }
                    await matchRepository.AddMatchAsync(match, ct);
                    await db.SaveChangesAsync(ct);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "[Refresh] Error persisting match {MatchId} for {GameName}#{Tag}", matchId, gameName, tagLine);
                }
            }

            logger.LogInformation("[Refresh] Completed refresh for {GameName}#{Tag} on {Platform}", gameName, tagLine, platformRoute);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[Refresh] Error refreshing {GameName}#{Tag} on {Platform}", gameName, tagLine, platformRoute);
            throw;
        }
        finally
        {
            // Always release the lock
            try
            {
                await refreshLockRepository.ReleaseAsync(lockKey, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[Refresh] Failed to release refresh lock {LockKey}", lockKey);
            }
        }
    }
}
