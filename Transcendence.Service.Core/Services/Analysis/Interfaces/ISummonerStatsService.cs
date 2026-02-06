using Transcendence.Service.Core.Services.Analysis.Models;
using Transcendence.Service.Core.Services.RiotApi.DTOs;

namespace Transcendence.Service.Core.Services.Analysis.Interfaces;

public interface ISummonerStatsService
{
    Task<SummonerOverviewStats> GetSummonerOverviewAsync(Guid summonerId, int recentGamesCount, CancellationToken ct);
    Task<IReadOnlyList<ChampionStat>> GetChampionStatsAsync(Guid summonerId, int top, CancellationToken ct);
    Task<IReadOnlyList<RoleStat>> GetRoleBreakdownAsync(Guid summonerId, CancellationToken ct);

    Task<PagedResult<RecentMatchSummary>> GetRecentMatchesAsync(Guid summonerId, int page, int pageSize,
        CancellationToken ct);

    /// <summary>
    /// Gets full match details including all participants with items, runes, and spells.
    /// </summary>
    /// <param name="matchId">The Riot match ID (e.g., "NA1_1234567890")</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Full match details or null if match not found</returns>
    Task<MatchDetailDto?> GetMatchDetailAsync(string matchId, CancellationToken ct);
}