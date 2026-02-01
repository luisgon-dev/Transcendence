using Microsoft.EntityFrameworkCore;
using Transcendence.Data.Models.LoL.Match;
using Transcendence.Data.Repositories.Interfaces;

namespace Transcendence.Data.Repositories.Implementations;

public class MatchRepository(TranscendenceContext transcendenceContext) : IMatchRepository
{
    public async Task AddMatchAsync(Match match, CancellationToken cancellationToken)
    {
        await transcendenceContext.Matches.AddAsync(match, cancellationToken);
    }

    public Task<Match?> GetMatchByIdAsync(string matchId, CancellationToken cancellationToken)
    {
        return transcendenceContext.Matches.FirstOrDefaultAsync(x => x.MatchId == matchId, cancellationToken);
    }
}