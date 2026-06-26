using Microsoft.EntityFrameworkCore;
using Transcendence.Data.Models.LoL.Analytics;
using Transcendence.Service.Core.Services.Analytics.Models;

namespace Transcendence.Service.Core.Services.Analytics.Implementations;

/// <summary>
/// Stats-backed read path: serves win-rates and the tier list from the precomputed aggregate tables
/// (<see cref="ChampionRoleTierStat"/>, <see cref="ScopeMatchCountStat"/>, <see cref="ChampionBanScopeStat"/>)
/// instead of scanning raw <c>MatchParticipants</c>. The atoms are rolled up to the requested scope in SQL
/// (SUM over the regions/tiers in scope; distinct-match ban metrics by point lookup), then the identical
/// downstream logic of the raw path (shared private helpers) produces the same DTOs. Falls back to the raw
/// compute for any patch that has no aggregates yet (rollout / brand-new patch), so reads are always safe.
/// </summary>
public partial class ChampionAnalyticsComputeService
{
    public async Task<List<ChampionWinRateDto>> ComputeWinRatesFromStatsAsync(
        int championId,
        ChampionAnalyticsFilter filter,
        string patch,
        CancellationToken ct)
    {
        if (!await HasStatsAsync(patch, ct))
            return await ComputeWinRatesAsync(championId, filter, patch, ct);

        var minimumGamesRequired = await GetAdaptiveMinimumGamesRequiredAsync(patch, ct);
        var rankTierScope = AnalyticsScopeMath.ParseRankTierScope(filter.RankTier);
        var scopeToken = AnalyticsScopeMath.ScopeTokenOf(rankTierScope);
        var tierFilter = RankTierCatalog.ResolveScopeTiers(scopeToken);
        var region = filter.Region;                                  // already normalized to a platform or null (ALL)
        var roleFilter = string.IsNullOrEmpty(filter.Role) ? null : filter.Role;

        // Champion's per-(role, tier) Games/Wins, summed over the platform regions in scope.
        var champQuery = _context.ChampionRoleTierStats.AsNoTracking()
            .Where(x => x.Patch == patch && x.ChampionId == championId);
        champQuery = ApplyStatScope(champQuery, region, tierFilter);
        if (roleFilter != null)
            champQuery = champQuery.Where(x => x.Role == roleFilter);

        var champRows = await champQuery
            .GroupBy(x => new { x.Role, x.RankTier })
            .Select(g => new { g.Key.Role, g.Key.RankTier, Games = g.Sum(x => x.Games), Wins = g.Sum(x => x.Wins) })
            .ToListAsync(ct);

        var totalGames = champRows.Sum(x => x.Games);
        if (totalGames == 0)
            return [];

        var effectiveMinimumGames = AnalyticsScopeMath.ResolveEffectiveSampleSize(minimumGamesRequired, totalGames, floor: 3);
        var winRateData = champRows.Where(x => x.Games >= effectiveMinimumGames).ToList();
        if (winRateData.Count == 0)
            winRateData = champRows.Where(x => x.Games >= 1).ToList();

        // Champion-level, role-independent ban rate by point lookup (matching the live BuildScopedMatchIdQuery,
        // which never role-filters). Region=ALL reads the synthetic "ALL" row; never summed.
        var banRate = await LookupBanRateAsync(patch, region, scopeToken, championId, ct);

        // Standings (role-rank + pick-rate denominator): every champion in the same (role, tier) scope.
        var relevantRoles = winRateData.Select(x => x.Role).Distinct().ToList();
        var standingsQuery = ApplyStatScope(
            _context.ChampionRoleTierStats.AsNoTracking().Where(x => x.Patch == patch && relevantRoles.Contains(x.Role)),
            region, tierFilter);

        var standingsRows = await standingsQuery
            .GroupBy(x => new { x.Role, x.RankTier, x.ChampionId })
            .Select(g => new { g.Key.Role, g.Key.RankTier, g.Key.ChampionId, Games = g.Sum(x => x.Games), Wins = g.Sum(x => x.Wins) })
            .ToListAsync(ct);

        var standingsByRoleTier = standingsRows
            .GroupBy(x => (Role: x.Role, Tier: x.RankTier))
            .ToDictionary(
                g => g.Key,
                g => new
                {
                    Ranked = g
                        .OrderByDescending(x => x.Games > 0 ? (double)x.Wins / x.Games : 0.0)
                        .ThenByDescending(x => x.Games)
                        .ThenBy(x => x.ChampionId)
                        .Select(x => x.ChampionId)
                        .ToList(),
                    TotalGames = g.Sum(x => x.Games)
                });

        var result = new List<ChampionWinRateDto>(winRateData.Count);
        foreach (var data in winRateData)
        {
            var isUnranked = string.Equals(data.RankTier, RankTierCatalog.Unranked, StringComparison.OrdinalIgnoreCase);
            standingsByRoleTier.TryGetValue((data.Role, data.RankTier), out var standing);
            var roleTotalGames = standing?.TotalGames ?? 0;

            int? roleRank = null;
            int? rolePopulation = null;
            if (!isUnranked && standing != null)
            {
                rolePopulation = standing.Ranked.Count;
                var index = standing.Ranked.IndexOf(championId);
                roleRank = index >= 0 ? index + 1 : null;
            }

            result.Add(new ChampionWinRateDto(
                ChampionId: championId,
                Role: data.Role,
                RankTier: data.RankTier,
                Games: data.Games,
                Wins: data.Wins,
                WinRate: data.Games > 0 ? (double)data.Wins / data.Games : 0.0,
                PickRate: roleTotalGames > 0 ? (double)data.Games / roleTotalGames : 0.0,
                BanRate: banRate,
                RoleRank: roleRank,
                RolePopulation: rolePopulation,
                Patch: patch));
        }

        return result
            .OrderByDescending(x => x.Games)
            .ThenBy(x => x.Role, StringComparer.Ordinal)
            .ThenBy(x => x.RankTier, StringComparer.Ordinal)
            .ToList();
    }

    public async Task<List<TierListEntry>> ComputeTierListFromStatsAsync(
        string? role,
        string? rankTier,
        string? region,
        string patch,
        CancellationToken ct)
    {
        if (!await HasStatsAsync(patch, ct))
            return await ComputeTierListAsync(role, rankTier, region, patch, ct);

        var normalizedRole = string.IsNullOrWhiteSpace(role) ? "ALL" : role.ToUpperInvariant();
        var isUnifiedRole = normalizedRole == "ALL";
        var rankTierScope = AnalyticsScopeMath.ParseRankTierScope(rankTier);
        var scopeToken = AnalyticsScopeMath.ScopeTokenOf(rankTierScope);
        var tierFilter = RankTierCatalog.ResolveScopeTiers(scopeToken);
        var minimumGamesRequired = await GetAdaptiveMinimumGamesRequiredAsync(patch, ct);
        var regionFilter = AnalyticsRegionCatalog.NormalizeToFilter(region);

        var query = ApplyStatScope(
            _context.ChampionRoleTierStats.AsNoTracking().Where(x => x.Patch == patch),
            regionFilter, tierFilter);
        if (!isUnifiedRole)
            query = query.Where(x => x.Role == normalizedRole);

        // Aggregate per champion (unified) or per (champion, role), summed over the tiers/regions in scope.
        List<(int ChampionId, string TeamPosition, int Games, int Wins)> aggregated;
        if (isUnifiedRole)
        {
            aggregated = (await query
                .GroupBy(x => x.ChampionId)
                .Select(g => new { ChampionId = g.Key, Games = g.Sum(x => x.Games), Wins = g.Sum(x => x.Wins) })
                .ToListAsync(ct))
                .Select(g => (g.ChampionId, "ALL", g.Games, g.Wins))
                .ToList();
        }
        else
        {
            aggregated = (await query
                .GroupBy(x => new { x.ChampionId, x.Role })
                .Select(g => new { g.Key.ChampionId, g.Key.Role, Games = g.Sum(x => x.Games), Wins = g.Sum(x => x.Wins) })
                .ToListAsync(ct))
                .Select(g => (g.ChampionId, g.Role, g.Games, g.Wins))
                .ToList();
        }

        var totalParticipants = aggregated.Sum(x => x.Games);
        if (totalParticipants == 0)
            return [];

        var effectiveMinimumGames = AnalyticsScopeMath.ResolveEffectiveSampleSize(minimumGamesRequired, totalParticipants, floor: 5);
        var championStats = aggregated.Where(x => x.Games >= effectiveMinimumGames).ToList();
        if (championStats.Count == 0)
            championStats = aggregated.Where(x => x.Games >= 1).ToList();
        if (championStats.Count == 0)
            return [];

        // Ban counts by champion — role-independent point lookup (consistent with the win-rate page).
        var regionKey = regionFilter ?? PrecomputedAnalyticsRefresher.AllRegion;
        var totalMatchesInScope = await _context.ScopeMatchCountStats.AsNoTracking()
            .Where(x => x.Patch == patch && x.PlatformRegion == regionKey && x.RankScope == scopeToken)
            .Select(x => (int?)x.TotalMatches)
            .FirstOrDefaultAsync(ct) ?? 0;
        var banCountsByChampion = totalMatchesInScope == 0
            ? new Dictionary<int, int>()
            : await _context.ChampionBanScopeStats.AsNoTracking()
                .Where(x => x.Patch == patch && x.PlatformRegion == regionKey && x.RankScope == scopeToken)
                .ToDictionaryAsync(x => x.ChampionId, x => x.BannedMatches, ct);

        var withScores = championStats.Select(c => new
        {
            c.ChampionId,
            c.TeamPosition,
            c.Games,
            WinRate = c.Games > 0 ? (double)c.Wins / c.Games : 0.0,
            ConservativeWinRate = AnalyticsScopeMath.ComputeWilsonLowerBound(c.Wins, c.Games),
            PickRate = totalParticipants > 0 ? (double)c.Games / totalParticipants : 0.0,
            BanRate = totalMatchesInScope > 0
                ? (double)banCountsByChampion.GetValueOrDefault(c.ChampionId) / totalMatchesInScope
                : 0.0
        })
        .Select(c => new
        {
            c.ChampionId,
            c.TeamPosition,
            c.Games,
            c.WinRate,
            c.ConservativeWinRate,
            c.PickRate,
            c.BanRate,
            CompositeScore = (c.ConservativeWinRate * 0.70) + (c.PickRate * 0.30)
        })
        .OrderByDescending(x => x.CompositeScore)
        .ThenBy(x => x.ChampionId)
        .ToList();

        var total = withScores.Count;
        return withScores.Select((entry, index) =>
        {
            var percentile = (double)index / total;
            var tier = percentile switch
            {
                < 0.10 => TierGrade.S,
                < 0.30 => TierGrade.A,
                < 0.60 => TierGrade.B,
                < 0.85 => TierGrade.C,
                _ => TierGrade.D
            };

            return new TierListEntry(
                entry.ChampionId,
                entry.TeamPosition,
                tier,
                entry.CompositeScore,
                entry.WinRate,
                entry.PickRate,
                entry.BanRate,
                entry.Games,
                null,
                null);
        }).ToList();
    }

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

    public async Task<ChampionBuildsResponse> ComputeBuildsFromStatsAsync(
        int championId,
        string role,
        string? rankTier,
        string? region,
        string patch,
        CancellationToken ct)
    {
        var regionFilter = AnalyticsRegionCatalog.NormalizeToFilter(region);
        var scopeToken = AnalyticsScopeMath.ScopeTokenOf(AnalyticsScopeMath.ParseRankTierScope(rankTier));

        // Only EMERALD_PLUS + ALL are precomputed, at the all-region scope; a specific tier/region (or a
        // missing/un-refreshed snapshot) falls back to the live build compute.
        if (regionFilter == null &&
            (scopeToken == RankTierCatalog.EmeraldPlusScope || scopeToken == RankTierCatalog.AllScope))
        {
            var payload = await _context.ChampionBuildSnapshots.AsNoTracking()
                .Where(x => x.Patch == patch && x.ChampionId == championId && x.Role == role && x.RankScope == scopeToken)
                .Select(x => x.Payload)
                .FirstOrDefaultAsync(ct);

            if (payload != null)
            {
                var cached = BuildSnapshotSerialization.Deserialize(payload);
                if (cached != null)
                    return cached;
            }
        }

        return await ComputeBuildsAsync(championId, role, rankTier, region, patch, ct);
    }

    public Task<DateTime?> GetAnalyticsComputedAtAsync(string patch, CancellationToken ct) =>
        _context.ChampionRoleTierStats
            .AsNoTracking()
            .Where(x => x.Patch == patch)
            .OrderByDescending(x => x.ComputedAtUtc)
            .Select(x => (DateTime?)x.ComputedAtUtc)
            .FirstOrDefaultAsync(ct);

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

    private Task<bool> HasStatsAsync(string patch, CancellationToken ct) =>
        _context.ChampionRoleTierStats.AsNoTracking().AnyAsync(x => x.Patch == patch, ct);

    private Task<bool> HasMatchupStatsAsync(string patch, CancellationToken ct) =>
        _context.ChampionMatchupStats.AsNoTracking().AnyAsync(x => x.Patch == patch, ct);

    /// <summary>Applies the region (null = ALL → sum every platform) + tier-scope (null = all tiers) filters.</summary>
    private static IQueryable<ChampionRoleTierStat> ApplyStatScope(
        IQueryable<ChampionRoleTierStat> query, string? region, IReadOnlyList<string>? tierFilter)
    {
        if (region != null)
            query = query.Where(x => x.PlatformRegion == region);
        if (tierFilter != null)
            query = query.Where(x => tierFilter.Contains(x.RankTier));
        return query;
    }

    private async Task<double> LookupBanRateAsync(
        string patch, string? region, string scopeToken, int championId, CancellationToken ct)
    {
        var regionKey = region ?? PrecomputedAnalyticsRefresher.AllRegion;
        var totalMatchesInScope = await _context.ScopeMatchCountStats.AsNoTracking()
            .Where(x => x.Patch == patch && x.PlatformRegion == regionKey && x.RankScope == scopeToken)
            .Select(x => (int?)x.TotalMatches)
            .FirstOrDefaultAsync(ct) ?? 0;
        if (totalMatchesInScope == 0)
            return 0.0;

        var bannedMatches = await _context.ChampionBanScopeStats.AsNoTracking()
            .Where(x => x.Patch == patch && x.PlatformRegion == regionKey && x.RankScope == scopeToken && x.ChampionId == championId)
            .Select(x => (int?)x.BannedMatches)
            .FirstOrDefaultAsync(ct) ?? 0;
        return (double)bannedMatches / totalMatchesInScope;
    }
}
