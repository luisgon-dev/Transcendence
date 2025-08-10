using Camille.Enums;
using ProjectSyndraBackend.Data.Models.LoL.Account;

namespace ProjectSyndraBackend.Service.Services.RiotApi.Interfaces;

public interface IRankService
{
    Task<List<Rank>> GetRankedDataAsync(string summonerPuuid, PlatformRoute platformRoute,
        CancellationToken cancellationToken = default);
}