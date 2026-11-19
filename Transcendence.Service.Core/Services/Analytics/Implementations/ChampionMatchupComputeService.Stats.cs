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
        var scope = new ChampionSectionScope(patch, AnalyticsQueueCatalog.Normalize(queueFamily), region, rankTier, role, championId);
        var snapshot = await ReadMatchupsSnapshotAsync(scope, allowLegacyEmpty: true, ct);
        return snapshot?.Data ?? await ComputeMatchupsAsync(championId, role, rankTier, region, scope.QueueFamily, patch, ct);
    }

    public Task<ChampionScopedSection<ChampionMatchupsResponse>?> ReadMatchupsSnapshotAsync(
        ChampionSectionScope scope, CancellationToken ct) =>
        ReadMatchupsSnapshotAsync(scope, allowLegacyEmpty: false, ct);

    private async Task<ChampionScopedSection<ChampionMatchupsResponse>?> ReadMatchupsSnapshotAsync(
        ChampionSectionScope scope, bool allowLegacyEmpty, CancellationToken ct)
    {
        if (scope.QueueFamily != QueueCatalog.QueueFamilyRankedSoloDuo ||
            AnalyticsRegionCatalog.NormalizeToFilter(scope.Region) != null)
            return null;

        var snapshot = await _context.ChampionMatchupSnapshots
            .AsNoTracking()
            .Where(snapshot =>
                snapshot.Patch == scope.Patch &&
                snapshot.IsActive &&
                snapshot.Status == Data.Models.LoL.Analytics.ChampionMatchupSnapshotStatus.Ready)
            .Select(snapshot => new { snapshot.Id, snapshot.SourceCutoffUtc })
            .FirstOrDefaultAsync(ct);
        if (snapshot is null && allowLegacyEmpty && !await _context.ChampionMatchupStats
                .AsNoTracking().AnyAsync(stat => stat.Patch == scope.Patch && stat.SnapshotId == null, ct))
            return null;

        var activeSnapshotId = (Guid?)snapshot?.Id;
        var rankTier = scope.RankTier;
        var region = scope.Region;
        var patch = scope.Patch;
        var championId = scope.ChampionId;
        var role = scope.Role!;
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

        if (rolled.Count == 0 && !allowLegacyEmpty)
            return null;

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

        return new(ChampionSectionState.Ready, snapshot?.SourceCutoffUtc,
            BuildMatchupsResponse(championId, role, rankTierScope, normalizedRegion, patch, aggregates));
    }

}
