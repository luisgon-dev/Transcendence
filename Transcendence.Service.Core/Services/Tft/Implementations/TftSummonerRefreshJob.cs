using Camille.Enums;
using Hangfire;
using Transcendence.Data;
using Transcendence.Data.Repositories.Interfaces;
using Transcendence.Service.Core.Services.RiotApi;
using Transcendence.Service.Core.Services.Tft.Interfaces;

namespace Transcendence.Service.Core.Services.Tft.Implementations;

public class TftSummonerRefreshJob(
    ITftSummonerService summonerService,
    ITftSummonerRepository summonerRepository,
    ITftMatchRepository matchRepository,
    ITftMatchService matchService,
    ITftMatchIdsClient matchIdsClient,
    TranscendenceContext db,
    IRefreshLockRepository refreshLockRepository,
    ILogger<TftSummonerRefreshJob> logger) : ITftSummonerRefreshJob
{
    [Queue("tft-refresh-high")]
    public async Task RefreshByRiotId(
        string gameName,
        string tagLine,
        PlatformRoute platformRoute,
        string lockKey,
        string? priorityLockKey,
        CancellationToken ct = default)
    {
        try
        {
            var summoner = await summonerService.GetSummonerByRiotIdAsync(gameName, tagLine, platformRoute, ct);
            var persisted = await summonerRepository.AddOrUpdateAsync(summoner, ct);

            var matchIds = await matchIdsClient.GetMatchIdsByPuuidAsync(
                platformRoute.ToRegional(),
                persisted.Puuid!,
                count: 20,
                endTimeEpochSeconds: null,
                start: 0,
                startTimeEpochSeconds: null,
                ct);

            var existing = await matchRepository.GetExistingMatchIdsAsync(matchIds, ct);
            foreach (var matchId in matchIds.Where(x => !existing.Contains(x)))
            {
                var match = await matchService.GetMatchDetailsAsync(matchId, platformRoute.ToRegional(), platformRoute, ct);
                if (match == null)
                    continue;

                await matchRepository.AddMatchAsync(match, ct);
            }

            await db.SaveChangesAsync(ct);
        }
        catch (RiotAccountNotFoundException ex)
        {
            // Riot id no longer resolves (deleted/renamed/reassigned). Permanent — skip without
            // failing so Hangfire does not retry this dead entry indefinitely.
            logger.LogWarning(ex, "Skipping TFT refresh for {GameName}#{TagLine} on {Platform}: account no longer resolves.", gameName, tagLine, platformRoute);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed TFT refresh for {GameName}#{TagLine} on {Platform}", gameName, tagLine, platformRoute);
            throw;
        }
        finally
        {
            await refreshLockRepository.ReleaseAsync(lockKey, ct);
            if (!string.IsNullOrWhiteSpace(priorityLockKey))
                await refreshLockRepository.ReleaseAsync(priorityLockKey, ct);
        }
    }
}
