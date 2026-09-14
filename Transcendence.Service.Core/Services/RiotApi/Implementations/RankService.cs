using Camille.Enums;
using Transcendence.Data.Models.LoL.Account;
using Transcendence.Service.Core.Services.RiotApi.Interfaces;

namespace Transcendence.Service.Core.Services.RiotApi.Implementations;

public class RankService(ILeagueEntriesClient leagueEntriesClient) : IRankService
{
    public async Task<List<Rank>> GetRankedDataAsync(string summonerPuuid, PlatformRoute platformRoute,
        CancellationToken cancellationToken = default)
    {
        var entries = await leagueEntriesClient.GetLeagueEntriesAsync(summonerPuuid, platformRoute, cancellationToken);

        // Normalize and return Rank models without binding to a Summoner; caller will attach.
        return entries.Select(e => new Rank
        {
            QueueType = e.QueueType ?? string.Empty,
            Tier = e.Tier ?? string.Empty,
            RankNumber = e.Rank ?? string.Empty,
            LeaguePoints = e.LeaguePoints,
            Wins = e.Wins,
            Losses = e.Losses
        }).ToList();
    }
}
