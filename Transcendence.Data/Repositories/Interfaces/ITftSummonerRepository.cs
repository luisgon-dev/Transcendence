using Transcendence.Data.Models.Tft.Account;

namespace Transcendence.Data.Repositories.Interfaces;

public interface ITftSummonerRepository
{
    Task<IReadOnlyList<TftSummonerSearchCandidate>> SearchByPrefixAsync(
        string platformRegion,
        string gameNamePrefix,
        string? tagLinePrefix,
        int limit,
        CancellationToken cancellationToken = default);

    Task<TftSummoner?> GetByPuuidAsync(
        string puuid,
        Func<IQueryable<TftSummoner>, IQueryable<TftSummoner>>? includes = null,
        CancellationToken cancellationToken = default);

    Task<TftSummoner?> FindByRiotIdAsync(
        string platformRegion,
        string gameName,
        string tagLine,
        Func<IQueryable<TftSummoner>, IQueryable<TftSummoner>>? includes = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds multiple summoners by their Riot IDs in a single query.
    /// </summary>
    Task<IReadOnlyList<TftSummoner>> FindByRiotIdsAsync(
        string platformRegion,
        IReadOnlyList<(string GameName, string TagLine)> riotIds,
        CancellationToken cancellationToken = default);

    Task<TftSummoner> AddOrUpdateAsync(TftSummoner summoner, CancellationToken cancellationToken = default);
}

public sealed record TftSummonerSearchCandidate(
    string PlatformRegion,
    string GameName,
    string TagLine,
    int ProfileIconId
);
