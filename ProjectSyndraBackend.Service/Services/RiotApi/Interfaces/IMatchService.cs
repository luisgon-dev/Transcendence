using Camille.Enums;
using ProjectSyndraBackend.Data.Models.Match;

namespace ProjectSyndraBackend.Service.Services.RiotApi.Interfaces;

public interface IMatchService
{
    Task<Match?> GetMatchDetailsAsync(string matchId, RegionalRoute regionalRoute, PlatformRoute platformRoute,
        CancellationToken cancellationToken = default);
}