using Camille.Enums;
using Camille.RiotGames;
using ProjectSyndraBackend.Data;
using ProjectSyndraBackend.Data.Repositories;
using ProjectSyndraBackend.Service.Services.RiotApi;

namespace ProjectSyndraBackend.Service.Services.Jobs;

// ReSharper disable once ClassNeverInstantiated.Global
public class AddOrUpdateHighEloProfiles(
    RiotGamesApi riotGamesApi,
    ProjectSyndraContext context,
    ILogger<AddOrUpdateHighEloProfiles> logger,
    ISummonerService summonerService,
    ISummonerRepository summonerRepository) : IJobTask
{
    public async Task Execute(CancellationToken stoppingToken)
    {
        var challengerLeague = await riotGamesApi.LeagueV4()
            .GetChallengerLeagueAsync(PlatformRoute.NA1, QueueType.RANKED_SOLO_5x5, stoppingToken);
        var grandmasterLeague = await riotGamesApi.LeagueV4()
            .GetGrandmasterLeagueAsync(PlatformRoute.NA1, QueueType.RANKED_SOLO_5x5, stoppingToken);
        var masterLeague = await riotGamesApi.LeagueV4()
            .GetMasterLeagueAsync(PlatformRoute.NA1, QueueType.RANKED_SOLO_5x5, stoppingToken);


        // get all the summoner ID from the leagues into one list
        var summonerIds = challengerLeague.Entries.Select(x => x.SummonerId)
            .Concat(grandmasterLeague.Entries.Select(x => x.SummonerId))
            .Concat(masterLeague.Entries.Select(x => x.SummonerId))
            .ToList();


        foreach (var summonerId in summonerIds)
        {
            var summoner = await summonerService.GetSummonerByIdAsync(summonerId, PlatformRoute.NA1, stoppingToken);
            await summonerRepository.AddOrUpdateSummonerAsync(summoner, stoppingToken);
            logger.LogInformation("Summoner {SummonerName} added or updated", summoner.SummonerName);
        }

        logger.LogInformation("All summoners added or updated");
    }
}