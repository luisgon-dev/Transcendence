using Camille.Enums;
using Transcendence.Data.Models.LoL.Match;

namespace Transcendence.Service.Core.Services.RiotApi.Interfaces;

public interface IMatchService
{
    Task<Match?> GetMatchDetailsAsync(string matchId, RegionalRoute regionalRoute, PlatformRoute platformRoute,
        CancellationToken cancellationToken = default);

    Task<Match?> GetMatchDetailsLightweightAsync(string matchId, RegionalRoute regionalRoute,
        PlatformRoute platformRoute, CancellationToken cancellationToken = default);

    Task<bool> FetchMatchWithRetryAsync(string matchId, string region, CancellationToken cancellationToken = default);
}