using Camille.Enums;
using ProjectSyndraBackend.Data.Models.LoL.Match;

namespace ProjectSyndraBackend.Service.Services.RiotApi.Interfaces;

public interface IMatchService
{
    Task<Match?> GetMatchDetailsAsync(string matchId, RegionalRoute regionalRoute, PlatformRoute platformRoute,
        CancellationToken cancellationToken = default);
}