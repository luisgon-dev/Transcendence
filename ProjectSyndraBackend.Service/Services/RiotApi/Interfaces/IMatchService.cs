using Camille.Enums;
using ProjectSyndraBackend.Data;
using ProjectSyndraBackend.Data.Models.Match;

namespace ProjectSyndraBackend.Service.Services.RiotApi.Interfaces;

public interface IMatchService
{
    Task<Match?> GetMatchDetailsAsync(string matchId, RegionalRoute regionalRoute, PlatformRoute platformRoute, ProjectSyndraContext data, CancellationToken cancellationToken = default);
}