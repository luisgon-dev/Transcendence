using Transcendence.Service.Core.Services.Analysis.Models;
using Transcendence.Service.Core.Services.RiotApi.DTOs;

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

    Task<PagedResult<RecentMatchSummary>> GetRecentMatchesAsync(
        Guid summonerId,
        int page,
        int pageSize,
        string? queueFamily,
        IReadOnlyCollection<int>? queueIds,
        CancellationToken ct);

    /// <summary>
    /// Gets full match details including all participants with items, runes, and spells.
    /// </summary>
    /// <param name="matchId">The Riot match ID (e.g., "NA1_1234567890")</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Full match details or null if match not found</returns>
    Task<MatchDetailDto?> GetMatchDetailAsync(string matchId, CancellationToken ct);

    /// <summary>
    /// Gets the per-minute team gold/xp timeline for a match (the gold/xp-diff curve).
    /// Null if the match is unknown; Frames empty if no timeline snapshots exist.
    /// </summary>
    Task<MatchTimelineDto?> GetMatchTimelineAsync(string matchId, CancellationToken ct);
}
