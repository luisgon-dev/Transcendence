using Microsoft.EntityFrameworkCore;
using Transcendence.Data.Models.LoL.Match;
using Transcendence.Data.Repositories.Interfaces;

namespace Transcendence.Data.Repositories.Implementations;

public class MatchRepository(TranscendenceContext transcendenceContext) : IMatchRepository
{
    public async Task AddMatchAsync(Match match, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(match.MatchId))
            throw new ArgumentException("Match.MatchId must be populated before persistence.", nameof(match));

        if (transcendenceContext.Matches.Local.Any(x => x.MatchId == match.MatchId))
            return;

        var exists = await transcendenceContext.Matches
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(x => x.MatchId == match.MatchId, cancellationToken);

        if (exists)
            return;

        await transcendenceContext.Matches.AddAsync(match, cancellationToken);
    }

    public Task<Match?> GetMatchByIdAsync(string matchId, CancellationToken cancellationToken)
    {
        return transcendenceContext.Matches
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.MatchId == matchId, cancellationToken);
    }

    public async Task<HashSet<string>> GetExistingMatchIdsAsync(
        IEnumerable<string> matchIds,
        CancellationToken cancellationToken)
    {
        var normalizedIds = matchIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (normalizedIds.Length == 0)
            return new HashSet<string>(StringComparer.Ordinal);

        var existingIds = await transcendenceContext.Matches
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => normalizedIds.Contains(x.MatchId))
            .Select(x => x.MatchId)
            .ToListAsync(cancellationToken);

        return existingIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.Ordinal)!;
    }
}
