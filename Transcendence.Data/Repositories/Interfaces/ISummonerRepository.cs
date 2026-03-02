// ISummonerRepository.cs

using Transcendence.Data.Models.LoL.Account;

namespace Transcendence.Data.Repositories.Interfaces;

public interface ISummonerRepository
{
    Task<IReadOnlyList<SummonerSearchCandidate>> SearchByPrefixAsync(
        string platformRegion,
        string gameNamePrefix,
        string? tagLinePrefix,
        int limit,
        CancellationToken cancellationToken = default);

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

public sealed record SummonerSearchCandidate(
    string PlatformRegion,
    string GameName,
    string TagLine,
    int ProfileIconId
);
