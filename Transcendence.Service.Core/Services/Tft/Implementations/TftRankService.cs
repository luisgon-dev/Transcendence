using Camille.Enums;
using Camille.RiotGames;
using Transcendence.Data.Models.Tft.Account;
using Transcendence.Service.Core.Services.RiotApi;
using Transcendence.Service.Core.Services.Tft.Interfaces;

namespace Transcendence.Service.Core.Services.Tft.Implementations;

public class TftRankService(TftRiotApiContext riotApiContext) : ITftRankService
{
    public async Task<List<TftRank>> GetRankedDataAsync(string summonerPuuid, PlatformRoute platformRoute, CancellationToken ct = default)
    {
        var entries = await riotApiContext.Api.TftLeagueV1().GetLeagueEntriesByPUUIDAsync(platformRoute, summonerPuuid, ct);
        return entries.Select(entry => new TftRank
        {
            QueueType = entry.QueueType.ToString(),
            Tier = entry.Tier?.ToString() ?? entry.RatedTier ?? string.Empty,
            RankNumber = entry.Rank?.ToString() ?? string.Empty,
            LeaguePoints = entry.LeaguePoints ?? 0,
            Wins = entry.Wins,
            Losses = entry.Losses
        }).ToList();
    }
}
