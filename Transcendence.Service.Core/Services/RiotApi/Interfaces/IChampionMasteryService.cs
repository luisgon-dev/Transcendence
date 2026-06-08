using Camille.Enums;
using Transcendence.Data.Models.LoL.Account;

namespace Transcendence.Service.Core.Services.RiotApi.Interfaces;

public interface IChampionMasteryService
{
    Task<List<ChampionMastery>> GetMasteriesAsync(string summonerPuuid, PlatformRoute platformRoute,
        CancellationToken cancellationToken = default);
}
