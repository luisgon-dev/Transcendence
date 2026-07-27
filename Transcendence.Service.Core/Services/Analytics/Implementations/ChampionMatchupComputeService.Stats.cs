using Microsoft.EntityFrameworkCore;
using Transcendence.Service.Core.Services.Analytics.Models;
using Transcendence.Service.Core.Services.RiotApi;

namespace Transcendence.Service.Core.Services.Analytics.Implementations;

/// <summary>
/// Stats-backed read path for matchups: serves them from the precomputed <c>ChampionMatchupStat</c>
/// aggregates — rolled up in SQL to the requested rank scope — instead of recomputing from raw matches,
/// falling back to the live compute for any scope/patch without aggregates yet so reads are always safe.
/// (Win-rate and tier-list stats live in <c>ChampionWinRateComputeService</c>; builds in
/// <c>ChampionBuildComputeService</c>; the pro surfaces in <c>ChampionProComputeService</c>.)
/// </summary>
public partial class ChampionMatchupComputeService
{

    public async Task<ChampionMatchupsResponse> ComputeMatchupsFromStatsAsync(
        int championId,
        string role,
        string? rankTier,
        string? region,
        string patch,
        CancellationToken ct) =>
        await ComputeMatchupsFromStatsAsync(
            championId, role, rankTier, region, QueueCatalog.QueueFamilyRankedSoloDuo, patch, ct);

    public async Task<ChampionMatchupsResponse> ComputeMatchupsFromStatsAsync(
        int championId,
        string role,
        string? rankTier,
        string? region,
        string queueFamily,
        string patch,
        CancellationToken ct)
    {
        var normalizedQueue = AnalyticsQueueCatalog.Normalize(queueFamily);
        var regionFilter = AnalyticsRegionCatalog.NormalizeToFilter(region);
        var activeSnapshotId = normalizedQueue == QueueCatalog.QueueFamilyRankedSoloDuo && regionFilter == null
            ? await _context.ChampionMatchupSnapshots
                .AsNoTracking()
                .Where(snapshot =>
                    snapshot.Patch == patch &&
                    snapshot.IsActive &&
                    snapshot.Status == Data.Models.LoL.Analytics.ChampionMatchupSnapshotStatus.Ready)
                .Select(snapshot => (Guid?)snapshot.Id)
                .FirstOrDefaultAsync(ct)
            : null;
        var hasLegacyStats = activeSnapshotId == null &&
                             normalizedQueue == QueueCatalog.QueueFamilyRankedSoloDuo &&
                             regionFilter == null &&
                             await _context.ChampionMatchupStats
                                 .AsNoTracking()
                                 .AnyAsync(stat => stat.Patch == patch && stat.SnapshotId == null, ct);
        // Only the all-region scope is precomputed; a specific region or an un-refreshed patch falls back
        // to the raw self-join compute. During rollout, legacy rows remain readable until the first
        // complete generation is promoted; Building generations are never visible.
        if (normalizedQueue != QueueCatalog.QueueFamilyRankedSoloDuo ||
            regionFilter != null ||
            (activeSnapshotId == null && !hasLegacyStats))
            return await ComputeMatchupsAsync(championId, role, rankTier, region, normalizedQueue, patch, ct);

        var rankTierScope = AnalyticsScopeMath.ParseRankTierScope(rankTier);
        var scopeToken = AnalyticsScopeMath.ScopeTokenOf(rankTierScope);
        var tierFilter = RankTierCatalog.ResolveScopeTiers(scopeToken);
        var normalizedRegion = AnalyticsRegionCatalog.NormalizeOrDefault(region);

        var query = _context.ChampionMatchupStats.AsNoTracking()
            .Where(x =>
                x.Patch == patch &&
                x.SnapshotId == activeSnapshotId &&
                x.ChampionId == championId &&
                x.Role == role);
        if (tierFilter != null)
            query = query.Where(x => tierFilter.Contains(x.RankTier));

        // Roll the champion-tier atoms up to the requested rank scope (SUM the additive measures; MAX the
        // timeline freshness). Avg diffs are derived from the summed diff and the timeline-pair count.
        var rolled = await query
            .GroupBy(x => x.OpponentChampionId)
            .Select(g => new
            {
                OpponentChampionId = g.Key,
                Games = g.Sum(x => x.Games),
                Wins = g.Sum(x => x.Wins),
                TimelineGames = g.Sum(x => x.TimelineGames),
                SumGoldDiff = g.Sum(x => x.SumGoldDiffAt15),
                SumXpDiff = g.Sum(x => x.SumXpDiffAt15),
                LatestTimelineAtUtc = g.Max(x => x.LatestTimelineAtUtc)
            })
            .ToListAsync(ct);

        var aggregates = rolled
            .Select(m => new MatchupAggregate(
                m.OpponentChampionId,
                m.Games,
                m.Wins,
                m.Games - m.Wins,
                m.TimelineGames,
                m.TimelineGames > 0 ? (double?)((double)m.SumGoldDiff / m.TimelineGames) : null,
                m.TimelineGames > 0 ? (double?)((double)m.SumXpDiff / m.TimelineGames) : null,
                m.LatestTimelineAtUtc))
            .ToList();

        return BuildMatchupsResponse(championId, role, rankTierScope, normalizedRegion, patch, aggregates);
    }

}
