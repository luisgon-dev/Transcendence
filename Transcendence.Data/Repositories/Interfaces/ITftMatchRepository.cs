using Transcendence.Data.Models.Tft.Match;

namespace Transcendence.Data.Repositories.Interfaces;

public interface ITftMatchRepository
{
    Task AddMatchAsync(TftMatch match, CancellationToken cancellationToken = default);
    Task<TftMatch?> GetMatchByIdAsync(string matchId, CancellationToken cancellationToken = default);
    Task<HashSet<string>> GetExistingMatchIdsAsync(IEnumerable<string> matchIds, CancellationToken cancellationToken = default);
}
