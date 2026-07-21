using Transcendence.Service.Core.Services.Leaderboards.Models;

namespace Transcendence.Service.Core.Services.Leaderboards.Interfaces;

public interface ILeaderboardService
{
    Task<LeaderboardResponse> GetAsync(
        string platformRegion,
        string queue,
        int? championId,
        string? role,
        int limit,
        int minimumChampionGames,
        CancellationToken ct = default);
}
