// ISummonerRepository.cs
using ProjectSyndraBackend.Data.Models.Account;

namespace ProjectSyndraBackend.Data.Repositories;

public interface ISummonerRepository
{
    Task<Summoner?> GetSummonerByIdAsync(string summonerId, CancellationToken cancellationToken);
    Task<Summoner?> GetSummonerByPuuidAsync(string puuid, CancellationToken cancellationToken);
    Task AddOrUpdateSummonerAsync(Summoner summoner, CancellationToken cancellationToken);
}