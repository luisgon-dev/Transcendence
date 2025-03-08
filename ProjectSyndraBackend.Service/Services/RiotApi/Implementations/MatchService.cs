using Camille.Enums;
using ProjectSyndraBackend.Data;
using ProjectSyndraBackend.Data.Models.Match;
using ProjectSyndraBackend.Service.Services.RiotApi.Interfaces;

namespace ProjectSyndraBackend.Service.Services.RiotApi.Implementations;

public class MatchService : IMatchService
{
    public Task<Match?> GetMatchDetailsAsync(string matchId, RegionalRoute regionalRoute, PlatformRoute platformRoute,
        ProjectSyndraContext data, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }
}