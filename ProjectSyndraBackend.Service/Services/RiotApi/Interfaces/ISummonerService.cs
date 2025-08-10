using Camille.Enums;
using ProjectSyndraBackend.Data.Models.LoL.Account;

namespace ProjectSyndraBackend.Service.Services.RiotApi.Interfaces;

public interface ISummonerService
{


    Task<Summoner> GetSummonerByPuuidAsync(string puuid, PlatformRoute platformRoute,
        CancellationToken cancellationToken = default);
}