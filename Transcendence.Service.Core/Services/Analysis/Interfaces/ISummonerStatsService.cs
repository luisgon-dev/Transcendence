using Transcendence.Service.Core.Services.Analysis.Models;

namespace Transcendence.Service.Core.Services.Analysis.Interfaces;

public interface ISummonerStatsService
{
    Task<SummonerOverviewStats> GetSummonerOverviewAsync(Guid summonerId, int recentGamesCount, CancellationToken ct);
    Task<IReadOnlyList<ChampionStat>> GetChampionStatsAsync(Guid summonerId, int top, CancellationToken ct);
    Task<SummonerSeasonProfileStats> GetActiveSeasonProfileStatsAsync(
        Guid summonerId,
        int topChampions,
        int recentGamesCount,
        CancellationToken ct);
    Task<IReadOnlyList<RoleStat>> GetRoleBreakdownAsync(Guid summonerId, CancellationToken ct);

    /// <summary>
    /// Gets recorded rank snapshots (LP/tier progression) for a summoner, oldest first.
    /// </summary>
    Task<IReadOnlyList<RankHistoryEntry>> GetRankHistoryAsync(Guid summonerId, string? queueType, CancellationToken ct);

    /// <summary>
    /// Gets the summoners this player most frequently appears in matches with (from recent matches).
    /// </summary>
    Task<IReadOnlyList<PlayedWithEntry>> GetPlayedWithAsync(Guid summonerId, int recentMatches, int topCount, CancellationToken ct);

    /// <summary>
    /// Gets the summoner's highest champion-mastery entries (by points), served from stored data.
    /// </summary>
    Task<IReadOnlyList<ChampionMasteryEntry>> GetTopMasteryAsync(Guid summonerId, int top, CancellationToken ct);

}
