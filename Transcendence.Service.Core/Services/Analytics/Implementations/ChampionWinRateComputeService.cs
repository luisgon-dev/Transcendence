using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Transcendence.Data;
using Transcendence.Data.Models.LoL.Analytics;
using Transcendence.Service.Core.Queries;
using Transcendence.Service.Core.Services.Analytics.Interfaces;
using Transcendence.Service.Core.Services.Analytics.Models;

namespace Transcendence.Service.Core.Services.Analytics.Implementations;

/// <summary>
/// Raw + stats-backed computation for champion win rates and the tier list. Extracted from
/// <see cref="ChampionAnalyticsComputeService"/> (P10.1) so this domain is a focused unit; builds,
/// pro builds/playrate, and matchups stay on that service. Behavior is identical to the pre-extraction
/// code — the analytics test suite (raw + raw-vs-stats equivalence) is the gate.
/// </summary>
public sealed class ChampionWinRateComputeService : IChampionWinRateComputeService
{
    private readonly TranscendenceContext _context;
    private readonly ChampionAnalyticsComputeOptions _options;

    public ChampionWinRateComputeService(
        TranscendenceContext context,
        IOptions<ChampionAnalyticsComputeOptions> options)
    {
        _context = context;
        _options = options.Value;
    }

    /// <summary>
    /// Computes win rates for a champion across roles and rank tiers.
    /// Uses adaptive sample thresholds and degrades gracefully for early-patch datasets.
    /// </summary>
    public async Task<List<ChampionWinRateDto>> ComputeWinRatesAsync(
        int championId,
        ChampionAnalyticsFilter filter,
        string patch,
        CancellationToken ct)
    {
        var minimumGamesRequired = await AnalyticsSampleThreshold.ResolveAsync(_context, _options, patch, ct);
        var rankTierScope = AnalyticsScopeMath.ParseRankTierScope(filter.RankTier);

        // Base query: Match participants for this champion in this patch
        var baseQuery = _context.MatchParticipants
            .AsNoTracking()
            .Where(mp => mp.ChampionId == championId)
            .OnPatch(patch)
            .FromSuccessfulMatches()
            .InRankedSoloQueue()
            .WithAssignedRole();

        // Apply region filter if specified
        baseQuery = baseQuery.InPlatformRegion(filter.Region);

        // Apply role filter if specified
        if (!string.IsNullOrEmpty(filter.Role))
        {
            baseQuery = baseQuery.Where(mp => mp.TeamPosition == filter.Role);
        }

        var participantRanks = from mp in baseQuery
                               join rank in _context.Ranks
                                   .AsNoTracking()
                                   .Where(r => r.QueueType == "RANKED_SOLO_5x5")
                                   on mp.SummonerId equals rank.SummonerId into rankGroup
                               from soloRank in rankGroup.DefaultIfEmpty()
                               select new
                               {
                                   mp.TeamPosition,
                                   mp.Win,
                                   mp.MatchId,
                                   RankTier = soloRank != null ? soloRank.Tier : "UNRANKED"
                               };

        // Apply rank tier filter if specified
        if (rankTierScope.IsEmeraldPlus)
        {
            participantRanks = participantRanks
                .Where(pr => RankTierCatalog.EmeraldPlusTiers.Contains(pr.RankTier));
        }
        else if (!string.IsNullOrWhiteSpace(rankTierScope.ExactTier))
        {
            participantRanks = participantRanks
                .Where(pr => pr.RankTier == rankTierScope.ExactTier);
        }

        // P5.2: total games + the per-(role, tier) aggregation are computed in SQL instead of
        // materialising every participant row (~tens of thousands for a popular champion) and
        // grouping in memory. Only the handful of grouped rows come back.
        var totalGames = await participantRanks.CountAsync(ct);
        if (totalGames == 0)
            return [];

        var effectiveMinimumGames = AnalyticsScopeMath.ResolveEffectiveSampleSize(minimumGamesRequired, totalGames, floor: 3);

        var groupedData = await participantRanks
            .GroupBy(pr => new { pr.TeamPosition, pr.RankTier })
            .Select(g => new
            {
                Role = g.Key.TeamPosition!,
                RankTier = g.Key.RankTier,
                Games = g.Count(),
                Wins = g.Sum(pr => pr.Win ? 1 : 0)
            })
            .ToListAsync(ct);

        var winRateData = groupedData
            .Where(x => x.Games >= effectiveMinimumGames)
            .ToList();

        if (winRateData.Count == 0)
        {
            // Degrade gracefully so champion pages still show early-patch stats.
            winRateData = groupedData
                .Where(x => x.Games >= 1)
                .ToList();
        }

        // P7.1: champion-level ban rate over the rank-scoped match population (all champions),
        // matching ComputeTierListAsync — distinct matches in scope where this champion was banned
        // over total distinct matches in scope, uniform across the role rows. The previous code
        // intersected each group's *played* matches with this champion's *banned* matches, which is
        // structurally ~0 (a banned champion is never picked in that match), so ban rate always read
        // as 0. Stays in SQL: a subquery COUNT plus a Contains-subquery, no id set is materialised.
        var scopedMatchIds = BuildScopedMatchIdQuery(patch, filter.Region, rankTierScope);
        var totalMatchesInScope = await scopedMatchIds.CountAsync(ct);
        var bannedMatches = totalMatchesInScope == 0
            ? 0
            : await _context.MatchBans
                .AsNoTracking()
                .Where(b => b.ChampionId == championId && scopedMatchIds.Contains(b.MatchId))
                .Select(b => b.MatchId)
                .Distinct()
                .CountAsync(ct);
        var banRate = totalMatchesInScope > 0 ? (double)bannedMatches / totalMatchesInScope : 0.0;

        // Role rank + pick-rate denominator, batched. Previously each (role, tier) result row called
        // ComputeRoleRankAsync, which re-scanned + re-grouped the entire (role, tier) champion population
        // per row — an N+1 (5-10 full-population scans per champion, ×168 hourly in the warm job). Instead
        // compute the standings for every (role, tier) this champion appears in ONCE: the same scope as
        // participantRanks above but without the championId filter, grouped by (role, tier, champion).
        var relevantRoles = winRateData.Select(x => x.Role).Distinct().ToList();

        var populationRanks = from mp in _context.MatchParticipants
                    .AsNoTracking()
                    .OnPatch(patch)
                    .FromSuccessfulMatches()
                    .InRankedSoloQueue()
                    .WithAssignedRole()
                    .InPlatformRegion(filter.Region)
                    .Where(mp => relevantRoles.Contains(mp.TeamPosition!))
                join rank in _context.Ranks.AsNoTracking().Where(r => r.QueueType == "RANKED_SOLO_5x5")
                    on mp.SummonerId equals rank.SummonerId into rankGroup
                from soloRank in rankGroup.DefaultIfEmpty()
                select new
                {
                    mp.TeamPosition,
                    mp.ChampionId,
                    mp.Win,
                    RankTier = soloRank != null ? soloRank.Tier : "UNRANKED"
                };

        if (rankTierScope.IsEmeraldPlus)
            populationRanks = populationRanks.Where(pr => RankTierCatalog.EmeraldPlusTiers.Contains(pr.RankTier));
        else if (!string.IsNullOrWhiteSpace(rankTierScope.ExactTier))
            populationRanks = populationRanks.Where(pr => pr.RankTier == rankTierScope.ExactTier);

        var standingsRows = await populationRanks
            .GroupBy(pr => new { pr.TeamPosition, pr.RankTier, pr.ChampionId })
            .Select(g => new
            {
                g.Key.TeamPosition,
                g.Key.RankTier,
                g.Key.ChampionId,
                Games = g.Count(),
                Wins = g.Sum(x => x.Win ? 1 : 0)
            })
            .ToListAsync(ct);

        // Per (role, tier): champions ranked by win rate (then games, then id) → this champion's rank +
        // population; sum of games → the true pick-rate denominator. Identical to ComputeRoleRankAsync.
        var standingsByRoleTier = standingsRows
            .GroupBy(x => (Role: x.TeamPosition!, Tier: x.RankTier))
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
            // No competitive rank within the UNRANKED bucket (preserves prior behaviour); the pick-rate
            // denominator is still meaningful there.
            var isUnranked = string.Equals(data.RankTier, "UNRANKED", StringComparison.OrdinalIgnoreCase);
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
                // P7.1: true pick rate — this champion's games in the (role, tier) over ALL champions'
                // games in that same (role, tier) (roleTotalGames), matching ComputeTierListAsync's
                // Games/totalParticipants. (Was this champion's OWN total games — a role-distribution share.)
                PickRate: roleTotalGames > 0 ? (double)data.Games / roleTotalGames : 0.0,
                BanRate: banRate,
                RoleRank: roleRank,
                RolePopulation: rolePopulation,
                Patch: patch
            ));
        }

        return result
            .OrderByDescending(x => x.Games)
            .ThenBy(x => x.Role, StringComparer.Ordinal)
            .ThenBy(x => x.RankTier, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Computes tier list ranking champions by composite score.
    /// S = top 10%, A = 10-30%, B = 30-60%, C = 60-85%, D = 85%+
    /// </summary>
    public async Task<List<TierListEntry>> ComputeTierListAsync(
        string? role,
        string? rankTier,
        string? region,
        string patch,
        CancellationToken ct)
    {
        var normalizedRole = string.IsNullOrWhiteSpace(role) ? "ALL" : role.ToUpperInvariant();
        var isUnifiedRole = normalizedRole == "ALL";
        var rankTierScope = AnalyticsScopeMath.ParseRankTierScope(rankTier);
        var minimumGamesRequired = await AnalyticsSampleThreshold.ResolveAsync(_context, _options, patch, ct);
        var regionFilter = AnalyticsRegionCatalog.NormalizeToFilter(region);

        // Step 1: Build base query for match participants in this patch
        var baseQuery = _context.MatchParticipants
            .AsNoTracking()
            .OnPatch(patch)
            .FromSuccessfulMatches()
            .InRankedSoloQueue()
            .WithAssignedRole();

        // Apply role filter (if not unified "ALL")
        if (!isUnifiedRole)
        {
            baseQuery = baseQuery.Where(mp => mp.TeamPosition == normalizedRole);
        }

        baseQuery = baseQuery.InPlatformRegion(regionFilter);

        // Only apply rank join semantics when a tier filter is requested.
        // Unfiltered views intentionally keep unranked participants.
        baseQuery = AnalyticsScopeMath.ApplyRankTierScopeToParticipants(baseQuery, rankTierScope, _context.Ranks.AsNoTracking());

        var query = baseQuery.Select(mp => new { mp.ChampionId, mp.TeamPosition, mp.Win, mp.MatchId });
        var totalParticipants = await query.CountAsync(ct);
        if (totalParticipants == 0)
            return [];

        var scopeMatchIds = await query
            .Select(x => x.MatchId)
            .Distinct()
            .ToListAsync(ct);
        var totalMatchesInScope = scopeMatchIds.Count;
        var banCountsByChampion = totalMatchesInScope == 0
            ? new Dictionary<int, int>()
            : await _context.MatchBans
                .AsNoTracking()
                .Where(b => scopeMatchIds.Contains(b.MatchId))
                .GroupBy(b => b.ChampionId)
                .Select(g => new
                {
                    ChampionId = g.Key,
                    BannedMatches = g.Select(x => x.MatchId).Distinct().Count()
                })
                .ToDictionaryAsync(x => x.ChampionId, x => x.BannedMatches, ct);

        var effectiveMinimumGames = AnalyticsScopeMath.ResolveEffectiveSampleSize(minimumGamesRequired, totalParticipants, floor: 5);

        // Step 2: Aggregate champion stats
        var aggregatedChampionStats = isUnifiedRole
            ? await query
                .GroupBy(x => x.ChampionId)
                .Select(g => new
                {
                    ChampionId = g.Key,
                    TeamPosition = "ALL",
                    Games = g.Count(),
                    Wins = g.Count(x => x.Win)
                })
                .ToListAsync(ct)
            : await query
                .GroupBy(x => new { x.ChampionId, x.TeamPosition })
                .Select(g => new
                {
                    g.Key.ChampionId,
                    TeamPosition = g.Key.TeamPosition!,
                    Games = g.Count(),
                    Wins = g.Count(x => x.Win)
                })
                .ToListAsync(ct);

        var championStats = aggregatedChampionStats
            .Where(x => x.Games >= effectiveMinimumGames)
            .ToList();

        if (championStats.Count == 0)
        {
            // Degrade gracefully so tier lists still render while patch data is ramping.
            championStats = aggregatedChampionStats
                .Where(x => x.Games >= 1)
                .ToList();
        }

        if (championStats.Count == 0)
            return new List<TierListEntry>();

        // Step 3: Calculate composite scores
        var withScores = championStats.Select(c => new
        {
            c.ChampionId,
            c.TeamPosition,
            c.Games,
            c.Wins,
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
            // Composite: conservative win rate lower bound (70%) + pick rate (30%).
            CompositeScore = (c.ConservativeWinRate * 0.70) + (c.PickRate * 0.30)
        })
        // Deterministic tie-break: tiering is positional (percentile by index), so a stable order among
        // equal composite scores is required for reproducible grades and to match the stats-backed path.
        .OrderByDescending(x => x.CompositeScore)
        .ThenBy(x => x.ChampionId)
        .ToList();

        // Step 5: Assign percentile-based tiers.
        // Previous-patch movement is intentionally omitted from the hot path so
        // current-patch tier lists are not blocked by recursive recomputation.
        // Top 10% = S, 10-30% = A, 30-60% = B, 60-85% = C, 85%+ = D
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
                null
            );
        }).ToList();
    }

    // Distinct match IDs for the full (all-champion) rank-scoped ranked-solo population in a
    // patch/region — the denominator population for champion-level ban rate. Returned as IQueryable
    // so callers compose CountAsync / Contains in SQL without materialising the (large) id set.
    private IQueryable<Guid> BuildScopedMatchIdQuery(string patch, string? region, AnalyticsScopeMath.RankTierScope rankTierScope)
    {
        var scope = _context.MatchParticipants
            .AsNoTracking()
            .OnPatch(patch)
            .FromSuccessfulMatches()
            .InRankedSoloQueue()
            .WithAssignedRole();

        scope = scope.InPlatformRegion(region);

        scope = AnalyticsScopeMath.ApplyRankTierScopeToParticipants(scope, rankTierScope, _context.Ranks.AsNoTracking());

        return scope.Select(mp => mp.MatchId).Distinct();
    }

    public async Task<List<ChampionWinRateDto>> ComputeWinRatesFromStatsAsync(
        int championId,
        ChampionAnalyticsFilter filter,
        string patch,
        CancellationToken ct)
    {
        if (!await HasStatsAsync(patch, ct))
            return await ComputeWinRatesAsync(championId, filter, patch, ct);

        var minimumGamesRequired = await AnalyticsSampleThreshold.ResolveAsync(_context, _options, patch, ct);
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
        var minimumGamesRequired = await AnalyticsSampleThreshold.ResolveAsync(_context, _options, patch, ct);
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

    public Task<DateTime?> GetAnalyticsComputedAtAsync(string patch, CancellationToken ct) =>
        _context.ChampionRoleTierStats
            .AsNoTracking()
            .Where(x => x.Patch == patch)
            .OrderByDescending(x => x.ComputedAtUtc)
            .Select(x => (DateTime?)x.ComputedAtUtc)
            .FirstOrDefaultAsync(ct);

    // ---- shared helpers for the stats path ----

    private Task<bool> HasStatsAsync(string patch, CancellationToken ct) =>
        _context.ChampionRoleTierStats.AsNoTracking().AnyAsync(x => x.Patch == patch, ct);

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
