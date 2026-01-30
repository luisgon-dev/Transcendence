using Camille.Enums;
using Camille.RiotGames;
using Transcendence.Data.Models.LoL.Account;
using Transcendence.Service.Core.RiotApi.Interfaces;
namespace Transcendence.Service.Core.RiotApi.Implementations;

public class RankService(RiotGamesApi riotApi) : IRankService
{
    public async Task<List<Rank>> GetRankedDataAsync(string summonerPuuid, PlatformRoute platformRoute,
        CancellationToken cancellationToken = default)
    {
        var entries = await riotApi.LeagueV4().GetLeagueEntriesByPUUIDAsync(platformRoute, summonerPuuid, cancellationToken);
        // Normalize and return Rank models without binding to a Summoner; caller will attach
        return entries.Select(e => new Rank
        {
            QueueType = e.QueueType.ToString(),
            Tier = e.Tier.ToString() ?? string.Empty,
            RankNumber = e.Rank.ToString() ?? string.Empty,
            LeaguePoints = e.LeaguePoints,
            Wins = e.Wins,
            Losses = e.Losses
        }).ToList();
    }
}
