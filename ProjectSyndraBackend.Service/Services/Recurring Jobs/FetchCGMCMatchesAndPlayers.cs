using Camille.Enums;
using Camille.RiotGames;
using Microsoft.EntityFrameworkCore;
using ProjectSyndraBackend.Data;
using ProjectSyndraBackend.Service.Services.Extensions;

namespace ProjectSyndraBackend.Service.Services.Recurring_Jobs;

public class FetchCgmcMatchesAndPlayers(
    RiotGamesApi riotGamesApi,
    ProjectSyndraContext context,
    ILogger<FetchCgmcMatchesAndPlayers> logger) : ITaskService
{
    public async Task ExecuteAsync(CancellationToken stoppingToken)
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
            var summoner = await riotGamesApi.GetSummoner(summonerId, PlatformRoute.NA1, stoppingToken);
            var existingSummoner =
                await context.Summoners.FirstOrDefaultAsync(x => x.SummonerId == summoner.SummonerId, stoppingToken);
            if (existingSummoner == null)
            {
                context.Summoners.Add(summoner);
            }
            else
            {
                existingSummoner.Puuid = summoner.Puuid;
                existingSummoner.AccountId = summoner.AccountId;
                existingSummoner.ProfileIconId = summoner.ProfileIconId;
                existingSummoner.RevisionDate = summoner.RevisionDate;
                existingSummoner.SummonerLevel = summoner.SummonerLevel;
                existingSummoner.GameName = summoner.GameName;
                existingSummoner.TagLine = summoner.TagLine;
                existingSummoner.SummonerName = summoner.SummonerName;
            }

            await context.SaveChangesAsync(stoppingToken);
        }
    }

    public int Interval { get; } = 500000000;
}