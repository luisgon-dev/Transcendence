using Microsoft.EntityFrameworkCore;
using Transcendence.Service.Core.Services.Analytics.Models;

namespace Transcendence.Service.Core.Services.Analytics.Implementations;

/// <summary>
/// Stats-backed read path for matchups and pro builds/playrate: serves them from the durable
/// snapshot/aggregate tables (<c>ChampionMatchupStat</c>, <c>AnalyticsResponseSnapshot</c>) — rolled up in
/// SQL or read back as a stored response — instead of recomputing from raw matches, falling back to the
/// live compute for any scope/patch without a snapshot yet so reads are always safe. (Win-rate and
/// tier-list stats live in <c>ChampionWinRateComputeService</c>; builds in <c>ChampionBuildComputeService</c>.)
/// </summary>
public partial class ChampionAnalyticsComputeService
{

    public async Task<ChampionMatchupsResponse> ComputeMatchupsFromStatsAsync(
        int championId,
        string role,
        string? rankTier,
        string? region,
        string patch,
        CancellationToken ct)
    {
        var regionFilter = AnalyticsRegionCatalog.NormalizeToFilter(region);
        // Only the all-region scope is precomputed; a specific region or an un-refreshed patch falls back
        // to the raw self-join compute.
        if (regionFilter != null || !await HasMatchupStatsAsync(patch, ct))
            return await ComputeMatchupsAsync(championId, role, rankTier, region, patch, ct);

        var rankTierScope = AnalyticsScopeMath.ParseRankTierScope(rankTier);
        var scopeToken = AnalyticsScopeMath.ScopeTokenOf(rankTierScope);
        var tierFilter = RankTierCatalog.ResolveScopeTiers(scopeToken);
        var normalizedRegion = AnalyticsRegionCatalog.NormalizeOrDefault(region);

        var query = _context.ChampionMatchupStats.AsNoTracking()
            .Where(x => x.Patch == patch && x.ChampionId == championId && x.Role == role);
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

    public async Task<ChampionProBuildsResponse> ComputeProBuildsFromStatsAsync(
        int championId,
        string? region,
        string? role,
        string scope,
        string patch,
        CancellationToken ct)
    {
        var normalizedRegion = string.IsNullOrWhiteSpace(region) ? "ALL" : region.Trim().ToUpperInvariant();
        var normalizedRole = string.IsNullOrWhiteSpace(role) ? "ALL" : role.Trim().ToUpperInvariant();
        var normalizedScope = NormalizeProScope(scope);

        // Precomputed only at the all-region scope for a specific role; everything else falls back to live.
        if (normalizedRegion == "ALL" && normalizedRole != "ALL")
        {
            var key = $"{championId}:{normalizedRole}:{normalizedScope}";
            var payload = await _context.AnalyticsResponseSnapshots.AsNoTracking()
                .Where(x => x.Feature == AnalyticsSnapshotSerialization.ProBuildsFeature && x.ScopeKey == key && x.Patch == patch)
                .Select(x => x.Payload)
                .FirstOrDefaultAsync(ct);

            if (payload != null)
            {
                var cached = AnalyticsSnapshotSerialization.Deserialize<ChampionProBuildsResponse>(payload);
                if (cached != null)
                    return cached;
            }
        }

        return await ComputeProBuildsAsync(championId, region, role, scope, patch, ct);
    }

    public async Task<ProChampionPlayrateResponse> ComputeProChampionPlayrateFromStatsAsync(
        string? region,
        string scope,
        string patch,
        CancellationToken ct)
    {
        var normalizedRegion = string.IsNullOrWhiteSpace(region) ? "ALL" : region.Trim().ToUpperInvariant();
        var normalizedScope = NormalizeProScope(scope);

        if (normalizedRegion == "ALL")
        {
            var payload = await _context.AnalyticsResponseSnapshots.AsNoTracking()
                .Where(x => x.Feature == AnalyticsSnapshotSerialization.ProPlayrateFeature && x.ScopeKey == normalizedScope && x.Patch == patch)
                .Select(x => x.Payload)
                .FirstOrDefaultAsync(ct);

            if (payload != null)
            {
                var cached = AnalyticsSnapshotSerialization.Deserialize<ProChampionPlayrateResponse>(payload);
                if (cached != null)
                    return cached;
            }
        }

        return await ComputeProChampionPlayrateAsync(region, scope, patch, ct);
    }

    // ---- shared helpers for the stats path ----


    private Task<bool> HasMatchupStatsAsync(string patch, CancellationToken ct) =>
        _context.ChampionMatchupStats.AsNoTracking().AnyAsync(x => x.Patch == patch, ct);


}
