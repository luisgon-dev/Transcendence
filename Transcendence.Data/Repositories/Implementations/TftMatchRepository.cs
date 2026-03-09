using Microsoft.EntityFrameworkCore;
using Transcendence.Data.Models.Tft.Match;
using Transcendence.Data.Repositories.Interfaces;

namespace Transcendence.Data.Repositories.Implementations;

public class TftMatchRepository(TranscendenceContext context) : ITftMatchRepository
{
    public async Task AddMatchAsync(TftMatch match, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(match.MatchId))
            throw new ArgumentException("TFT match id is required.", nameof(match));

        if (context.TftMatches.Local.Any(x => x.MatchId == match.MatchId))
            return;

        var exists = await context.TftMatches
            .AsNoTracking()
            .AnyAsync(x => x.MatchId == match.MatchId, cancellationToken);
        if (exists)
            return;

        await context.TftMatches.AddAsync(match, cancellationToken);
    }

    public Task<TftMatch?> GetMatchByIdAsync(string matchId, CancellationToken cancellationToken = default)
    {
        return context.TftMatches.FirstOrDefaultAsync(x => x.MatchId == matchId, cancellationToken);
    }

    public async Task<HashSet<string>> GetExistingMatchIdsAsync(
        IEnumerable<string> matchIds,
        CancellationToken cancellationToken = default)
    {
        var ids = matchIds
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (ids.Length == 0)
            return new HashSet<string>(StringComparer.Ordinal);

        var existing = await context.TftMatches
            .AsNoTracking()
            .Where(x => ids.Contains(x.MatchId))
            .Select(x => x.MatchId)
            .ToListAsync(cancellationToken);

        return existing.ToHashSet(StringComparer.Ordinal);
    }
}
