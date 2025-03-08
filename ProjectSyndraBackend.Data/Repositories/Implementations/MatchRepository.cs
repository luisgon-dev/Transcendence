using Microsoft.EntityFrameworkCore;
using ProjectSyndraBackend.Data.Models.Match;
using ProjectSyndraBackend.Data.Repositories.Interfaces;

namespace ProjectSyndraBackend.Data.Repositories.Implementations;

public class MatchRepository(ProjectSyndraContext projectSyndraContext) : IMatchRepository
{
    public Task AddMatchAsync(Match match, CancellationToken cancellationToken)
    {
        projectSyndraContext.Matches.Add(match);
        return projectSyndraContext.SaveChangesAsync(cancellationToken);
    }

    public Task<Match?> GetMatchByIdAsync(string matchId, CancellationToken cancellationToken)
    {
        return projectSyndraContext.Matches.FirstOrDefaultAsync(x => x.MatchId == matchId, cancellationToken);
    }
}