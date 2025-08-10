using Camille.Enums;
using Camille.RiotGames;
using Transcendence.Data.Models.LoL.Account;
using Transcendence.Data.Repositories;
using Transcendence.Data.Repositories.Interfaces;
using Transcendence.Service.Services.RiotApi.Interfaces;

namespace Transcendence.Service.Services.RiotApi.Implementations;

public class RankService(RiotGamesApi riotApi, ISummonerRepository repository) : IRankService
{
    public async Task<List<Rank>> GetRankedDataAsync(string summonerPuuid, PlatformRoute platformRoute,
        CancellationToken cancellationToken = default)
    {
        var ranks = await riotApi.LeagueV4().GetLeagueEntriesByPUUIDAsync(platformRoute, summonerPuuid, cancellationToken);
        var summoner = await repository.GetSummonerByPuuidAsync(summonerPuuid, null, cancellationToken);
        return ranks.Select(rank => new Rank
        {
            QueueType = rank.QueueType.ToString(),
            Tier = rank.Tier.ToString() ?? string.Empty,
            RankNumber = rank.Rank.ToString() ?? string.Empty,
            LeaguePoints = rank.LeaguePoints,
            Wins = rank.Wins,
            Losses = rank.Losses,
            Summoner = summoner!
        }).ToList();
    }
}