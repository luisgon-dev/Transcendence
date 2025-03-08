using Camille.Enums;
using ProjectSyndraBackend.Data.Models.Account;

namespace ProjectSyndraBackend.Service.Services.RiotApi;

public interface ISummonerService
{
    Task<Summoner> GetSummonerByIdAsync(string summonerId, PlatformRoute platformRoute,
        CancellationToken cancellationToken = default);

    Task<Summoner> GetSummonerByPuuidAsync(string puuid, PlatformRoute platformRoute,
        CancellationToken cancellationToken = default);
}