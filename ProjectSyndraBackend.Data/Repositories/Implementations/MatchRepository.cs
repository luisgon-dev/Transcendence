using Microsoft.EntityFrameworkCore;
using ProjectSyndraBackend.Data.Models.LoL.Match;
using ProjectSyndraBackend.Data.Repositories.Interfaces;

namespace ProjectSyndraBackend.Data.Repositories.Implementations;

public class MatchRepository(ProjectSyndraContext projectSyndraContext) : IMatchRepository
{
    public async Task AddMatchAsync(Match match, CancellationToken cancellationToken)
    {
        await projectSyndraContext.Matches.AddAsync(match, cancellationToken);
    }

    public Task<Match?> GetMatchByIdAsync(string matchId, CancellationToken cancellationToken)
    {
        return projectSyndraContext.Matches.FirstOrDefaultAsync(x => x.MatchId == matchId, cancellationToken);
    }


}