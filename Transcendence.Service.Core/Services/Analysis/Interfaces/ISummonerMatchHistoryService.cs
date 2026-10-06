using Transcendence.Service.Core.Services.Analysis.Models;
using Transcendence.Service.Core.Services.RiotApi.DTOs;

namespace Transcendence.Service.Core.Services.Analysis.Interfaces;

/// <summary>
/// Reads a summoner's paged match history and renders immutable match detail/timeline resources. Kept
/// separate from aggregate summoner statistics so consumers depend only on the surface they use.
/// </summary>
public interface ISummonerMatchHistoryService
{
    Task<PagedResult<RecentMatchSummary>> GetRecentMatchesAsync(
        Guid summonerId,
        int page,
        int pageSize,
        string? queueFamily,
        IReadOnlyCollection<int>? queueIds,
        int? championId,
        bool includeFacets,
        CancellationToken ct);

    /// <summary>
    /// The date (Unix ms) of the summoner's newest stored match, or null when none is stored. Use this
    /// instead of loading a history page when only the date is needed.
    /// </summary>
    Task<long?> GetLatestMatchDateAsync(Guid summonerId, CancellationToken ct);

    Task<MatchDetailDto?> GetMatchDetailAsync(string matchId, CancellationToken ct);

    Task<MatchTimelineDto?> GetMatchTimelineAsync(string matchId, CancellationToken ct);
}
