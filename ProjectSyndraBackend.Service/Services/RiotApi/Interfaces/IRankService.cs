using Camille.Enums;
using ProjectSyndraBackend.Data.Models.Account;

namespace ProjectSyndraBackend.Service.Services.RiotApi.Interfaces;

public interface IRankService
{
    Task<List<Rank>> GetRankedDataAsync(string summonerId, PlatformRoute platformRoute,
        CancellationToken cancellationToken = default);
}