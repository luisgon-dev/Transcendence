using Camille.Enums;
using Transcendence.Data.Models.LoL.Account;

namespace Transcendence.Service.Services.RiotApi.Interfaces;

public interface ISummonerService
{


    Task<Summoner> GetSummonerByPuuidAsync(string puuid, PlatformRoute platformRoute,
        CancellationToken cancellationToken = default);
}