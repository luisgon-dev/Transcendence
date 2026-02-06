using Camille.Enums;
using Camille.RiotGames;
using Transcendence.Data;
using Transcendence.Data.Repositories.Interfaces;
using Transcendence.Service.Core.Services.RiotApi.Interfaces;

namespace Transcendence.Service.Core.Services.Jobs;

// ReSharper disable once ClassNeverInstantiated.Global
public class AddOrUpdateHighEloProfiles(
    RiotGamesApi riotGamesApi,
    TranscendenceContext context,
    ILogger<AddOrUpdateHighEloProfiles> logger,
    ISummonerService summonerService,
    ISummonerRepository summonerRepository)
{
    public async Task Execute(CancellationToken stoppingToken)
    {
        const int saveBatchSize = 50;
        var pendingChanges = 0;

        var challengerLeague = await riotGamesApi.LeagueV4()
            .GetChallengerLeagueAsync(PlatformRoute.NA1, QueueType.RANKED_SOLO_5x5, stoppingToken);
        var grandmasterLeague = await riotGamesApi.LeagueV4()
            .GetGrandmasterLeagueAsync(PlatformRoute.NA1, QueueType.RANKED_SOLO_5x5, stoppingToken);
        var masterLeague = await riotGamesApi.LeagueV4()
            .GetMasterLeagueAsync(PlatformRoute.NA1, QueueType.RANKED_SOLO_5x5, stoppingToken);


        // get all the summoner ID from the leagues into one list
        var summonerPuuids = challengerLeague.Entries.Select(x => x.Puuid)
            .Concat(grandmasterLeague.Entries.Select(x => x.Puuid))
            .Concat(masterLeague.Entries.Select(x => x.Puuid))
            .ToList();


        foreach (var summonerPuuid in summonerPuuids)
        {
            var summoner =
                await summonerService.GetSummonerByPuuidAsync(summonerPuuid, PlatformRoute.NA1, stoppingToken);
            await summonerRepository.AddOrUpdateSummonerAsync(summoner, stoppingToken);
            logger.LogInformation("Summoner {SummonerName} added or updated", summoner.SummonerName);
            pendingChanges++;

            if (pendingChanges < saveBatchSize)
                continue;

            await context.SaveChangesAsync(stoppingToken);
            pendingChanges = 0;
        }

        if (pendingChanges > 0)
            await context.SaveChangesAsync(stoppingToken);

        logger.LogInformation("All summoners added or updated");
    }
}
