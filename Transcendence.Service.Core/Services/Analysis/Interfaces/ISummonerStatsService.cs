using Transcendence.Service.Core.Analysis.Models;
namespace Transcendence.Service.Core.Analysis.Interfaces;

public interface ISummonerStatsService
{
    Task<SummonerOverviewStats> GetSummonerOverviewAsync(Guid summonerId, int recentGamesCount, CancellationToken ct);
    Task<IReadOnlyList<ChampionStat>> GetChampionStatsAsync(Guid summonerId, int top, CancellationToken ct);
    Task<IReadOnlyList<RoleStat>> GetRoleBreakdownAsync(Guid summonerId, CancellationToken ct);
    Task<PagedResult<RecentMatchSummary>> GetRecentMatchesAsync(Guid summonerId, int page, int pageSize, CancellationToken ct);
}
