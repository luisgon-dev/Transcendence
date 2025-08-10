using Camille.Enums;
using Transcendence.Data.Models.LoL.Match;

namespace Transcendence.Service.Services.RiotApi.Interfaces;

public interface IMatchService
{
    Task<Match?> GetMatchDetailsAsync(string matchId, RegionalRoute regionalRoute, PlatformRoute platformRoute,
        CancellationToken cancellationToken = default);
}