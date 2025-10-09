using Camille.Enums;
using Camille.RiotGames;
using Microsoft.EntityFrameworkCore;
using Transcendence.Data;
using Transcendence.Data.Repositories.Interfaces;
using Transcendence.Service.Services.RiotApi.Interfaces;

namespace Transcendence.Service.Services.Jobs;

// ReSharper disable once ClassNeverInstantiated.Global
public class FetchLatestMatchInformation(
    RiotGamesApi riotGamesApi,
    TranscendenceContext context,
    IMatchService matchService,
    IMatchRepository matchRepository,
    Transcendence.Service.Services.StaticData.Interfaces.IStaticDataService staticDataService,
    ILogger<FetchLatestMatchInformation> logger) : IJobTask
{
    public async Task Execute(CancellationToken stoppingToken)
    {
        // Ensure static data (latest patch) is up to date before fetching matches
        await staticDataService.UpdateStaticDataAsync(stoppingToken);

        // get match information for every summoner in the database
        //TODO: Add more sophisticated logic to only fetch matches for summoners that have not been updated in a while or high elo
        var summoners = await context.Summoners.ToListAsync(stoppingToken);

        foreach (var summoner in summoners)
        {
            // cast string to platform route
            var platformRoute = (PlatformRoute)Enum.Parse(typeof(PlatformRoute), summoner.PlatformRegion!);
            var totalMatchList = new List<string>();
            try
            {
                var matchList = await riotGamesApi.MatchV5()
                    .GetMatchIdsByPUUIDAsync(platformRoute.ToRegional(), summoner.Puuid!, 20, null,
                        Queue.SUMMONERS_RIFT_5V5_RANKED_SOLO, null, null, "ranked", stoppingToken);
                totalMatchList.AddRange(matchList);
            }
            catch (Exception e)
            {
                logger.LogError(e, "Error fetching match information for summoner {SummonerId}", summoner.Id);
                continue;
            }

            // for every match ID in the match list try to fetch it through match repo, if it exists, remove it from the list
            foreach (var matchId in totalMatchList.ToList())
            {
                var match = await matchRepository.GetMatchByIdAsync(matchId, stoppingToken);
                if (match != null) totalMatchList.Remove(matchId);
            }

            foreach (var matchId in totalMatchList)
            {
                try
                {
                    var match = await matchService.GetMatchDetailsAsync(matchId, platformRoute.ToRegional(),
                        platformRoute, stoppingToken);
                    if (match == null)
                    {
                        logger.LogInformation("Match {MatchId} failed to fetch", matchId);
                        continue;
                    }

                    await matchRepository.AddMatchAsync(match, stoppingToken);
                    
                    // save all changes
                    await context.SaveChangesAsync(stoppingToken);
                    
                }
                catch (Exception e)
                {
                    logger.LogError(e, "Error fetching match information for match {MatchId}", matchId);
                }

                logger.LogInformation("Fetched match information for match {MatchId}", matchId);
            }
        }
    }
}