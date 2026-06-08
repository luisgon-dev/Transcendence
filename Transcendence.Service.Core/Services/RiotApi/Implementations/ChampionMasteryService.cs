using Camille.Enums;
using Camille.RiotGames;
using Transcendence.Data.Models.LoL.Account;
using Transcendence.Service.Core.Services.RiotApi.Interfaces;

namespace Transcendence.Service.Core.Services.RiotApi.Implementations;

public class ChampionMasteryService(LeagueRiotApiContext riotApiContext) : IChampionMasteryService
{
    public async Task<List<ChampionMastery>> GetMasteriesAsync(string summonerPuuid, PlatformRoute platformRoute,
        CancellationToken cancellationToken = default)
    {
        var entries = await riotApiContext.Api.ChampionMasteryV4()
            .GetAllChampionMasteriesByPUUIDAsync(platformRoute, summonerPuuid, cancellationToken);

        // Normalize to entities without binding to a Summoner; the repository attaches the FK.
        return entries.Select(e => new ChampionMastery
        {
            ChampionId = (int)e.ChampionId,
            ChampionLevel = e.ChampionLevel,
            ChampionPoints = e.ChampionPoints,
            LastPlayTime = e.LastPlayTime,
            ChestGranted = e.ChestGranted ?? false,
            TokensEarned = e.TokensEarned
        }).ToList();
    }
}
