using Camille.Enums;
using Camille.RiotGames;
using ProjectSyndraBackend.Data.Models.Account;
using ProjectSyndraBackend.Service.Services.RiotApi.Interfaces;

namespace ProjectSyndraBackend.Service.Services.RiotApi.Implementations;

public class RankService(RiotGamesApi riotApi) : IRankService
{

    public async Task<List<Rank>> GetRankedDataAsync(string summonerId, PlatformRoute platformRoute, CancellationToken cancellationToken = default)
    {
        var ranks = await riotApi.LeagueV4().GetLeagueEntriesForSummonerAsync(platformRoute, summonerId, cancellationToken);
        return ranks.Select(rank => new Rank
        {
            SummonerId = summonerId,
            QueueType = rank.QueueType.ToString(),
            Tier = rank.Tier.ToString() ?? string.Empty,
            RankNumber = rank.Rank.ToString() ?? string.Empty,
            LeaguePoints = rank.LeaguePoints,
            Wins = rank.Wins,
            Losses = rank.Losses,
            Summoner = null!
        }).ToList();
    }
}