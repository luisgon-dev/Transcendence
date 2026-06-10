using Camille.Enums;
using Transcendence.Data.Models.LoL.Match;

namespace Transcendence.Service.Core.Services.RiotApi.Interfaces;

public interface IMatchService
{
    Task<Match?> GetMatchDetailsAsync(string matchId, RegionalRoute regionalRoute, PlatformRoute platformRoute,
        CancellationToken cancellationToken = default);

    Task<Match?> GetMatchDetailsLightweightAsync(string matchId, RegionalRoute regionalRoute,
        PlatformRoute platformRoute, CancellationToken cancellationToken = default);

    // Fetches many matches, overlapping the per-match Riot round-trips in parallel (bounded by
    // maxParallelFetches) while building the EF entity graphs strictly sequentially on the caller's
    // thread (the DbContext is not thread-safe). Returns one entry per input id, same order, with null
    // where the fetch or build failed.
    Task<IReadOnlyList<Match?>> GetMatchDetailsBatchAsync(IReadOnlyList<string> matchIds,
        RegionalRoute regionalRoute, PlatformRoute platformRoute, bool lightweight, int maxParallelFetches,
        CancellationToken cancellationToken = default);

    Task<bool> FetchMatchWithRetryAsync(string matchId, string region, CancellationToken cancellationToken = default);
}