using Transcendence.Service.Services.Analysis.Models;

namespace Transcendence.Service.Services.Analysis.Interfaces;

public interface ISummonerStatsService
{
    Task<SummonerOverviewStats> GetSummonerOverviewAsync(Guid summonerId, int recentGamesCount, CancellationToken ct);
    Task<IReadOnlyList<ChampionStat>> GetChampionStatsAsync(Guid summonerId, int top, CancellationToken ct);
    Task<IReadOnlyList<RoleStat>> GetRoleBreakdownAsync(Guid summonerId, CancellationToken ct);
    Task<PagedResult<RecentMatchSummary>> GetRecentMatchesAsync(Guid summonerId, int page, int pageSize, CancellationToken ct);
}