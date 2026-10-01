using Camille.Enums;
using Transcendence.Data.Models.LoL.Match;

namespace Transcendence.Service.Core.Services.RiotApi.Interfaces;

public interface IMatchService
{
    /// <summary>Prepare a match; null means unavailable, rate backpressure throws MatchPreparationDeferredException.</summary>
    Task<Match?> GetMatchDetailsAsync(string matchId, RegionalRoute regionalRoute, PlatformRoute platformRoute,
        CancellationToken cancellationToken = default);

    /// <summary>Prepare lightweight details with the same explicit deferred outcome as full preparation.</summary>
    Task<Match?> GetMatchDetailsLightweightAsync(string matchId, RegionalRoute regionalRoute,
        PlatformRoute platformRoute, CancellationToken cancellationToken = default);

    Task<bool> FetchMatchWithRetryAsync(string matchId, string region, CancellationToken cancellationToken = default);
}
