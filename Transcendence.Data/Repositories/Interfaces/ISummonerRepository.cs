// ISummonerRepository.cs

using Transcendence.Data.Models.LoL.Account;

namespace Transcendence.Data.Repositories.Interfaces;

public interface ISummonerRepository
{
    Task<Summoner?> GetSummonerByPuuidAsync(string puuid,
        Func<IQueryable<Summoner>, IQueryable<Summoner>>? includes = null,
        CancellationToken cancellationToken = default);

    Task<Summoner?> FindByRiotIdAsync(
        string platformRegion,
        string gameName,
        string tagLine,
        Func<IQueryable<Summoner>, IQueryable<Summoner>>? includes = null,
        CancellationToken cancellationToken = default);

    Task<Summoner> AddOrUpdateSummonerAsync(Summoner summoner, CancellationToken cancellationToken);
}
