using Camille.Enums;
using Camille.RiotGames;
using Microsoft.EntityFrameworkCore;
using ProjectSyndraBackend.Data;
using ProjectSyndraBackend.Data.Models.Match;
using ProjectSyndraBackend.Service.Services.Extensions;

namespace ProjectSyndraBackend.Service.Services.Recurring_Jobs;

public class FetchLatestMatchInformation(
    RiotGamesApi riotGamesApi,
    ProjectSyndraContext context,
    ILogger<FetchLatestMatchInformation> logger) : ITaskService
{
    public async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // get match information for every summoner in the database
        var summoners = await context.Summoners.ToListAsync(stoppingToken);

        foreach (var summoner in summoners)
        {
            var matchList = await riotGamesApi.MatchV5()
                .GetMatchIdsByPUUIDAsync(RegionalRoute.AMERICAS, summoner.Puuid, 20, null, Queue.SUMMONERS_RIFT_5V5_RANKED_SOLO, null, null, "ranked", stoppingToken);

            foreach (var matchId in matchList)
            {
                var match = await riotGamesApi.GetMatchDetails(matchId, RegionalRoute.AMERICAS, PlatformRoute.NA1, context, stoppingToken);
                
                if (match == null) continue;
                
            }

        }
    }

    public int Interval { get; } = 500000000;
}