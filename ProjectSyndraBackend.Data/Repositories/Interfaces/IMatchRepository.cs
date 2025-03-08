using ProjectSyndraBackend.Data.Models.Match;

namespace ProjectSyndraBackend.Data.Repositories.Interfaces;

public interface IMatchRepository
{
    Task AddMatchAsync(Match match, CancellationToken cancellationToken);
    Task<Match?> GetMatchByIdAsync(string matchId, CancellationToken cancellationToken);
}