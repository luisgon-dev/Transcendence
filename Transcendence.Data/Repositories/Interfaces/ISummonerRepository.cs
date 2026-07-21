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

    Task<Summoner?> FindByRiotIdWithRanksAsync(
        string platformRegion,
        string gameName,
        string tagLine,
        CancellationToken cancellationToken = default);

    Task<Summoner> AddOrUpdateSummonerAsync(Summoner summoner, CancellationToken cancellationToken);

    /// <summary>
    /// Batch lookup summoners by Riot ID pairs within a single region.
    /// Returns summoners with Ranks eagerly loaded.
    /// </summary>
    Task<IReadOnlyList<Summoner>> FindByRiotIdsAsync(
        string platformRegion,
        IReadOnlyList<(string GameName, string TagLine)> riotIds,
        CancellationToken cancellationToken = default);
}

public sealed record SummonerSearchCandidate(
    string PlatformRegion,
    string GameName,
    string TagLine,
    int ProfileIconId
);
