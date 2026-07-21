using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Transcendence.Data;
using Transcendence.Data.Models.LoL.Analytics;
using Transcendence.Service.Core.Queries;
using Transcendence.Service.Core.Services.Analytics;
using Transcendence.Service.Core.Services.Analytics.Interfaces;
using Transcendence.Service.Core.Services.Analytics.Models;

namespace Transcendence.Service.Core.Services.Analytics.Implementations;

/// <summary>
/// Raw + stats-backed computation for champion win rates and the tier list. Extracted from the original
/// analytics compute service (P10.1) so this domain is a focused unit; builds, pro builds/playrate, and
/// matchups (<see cref="ChampionMatchupComputeService"/>) live in their own services. Behavior is identical
/// to the pre-extraction code — the analytics test suite (raw + raw-vs-stats equivalence) is the gate.
/// </summary>
public sealed class ChampionWinRateComputeService : IChampionWinRateComputeService
{
    private readonly TranscendenceContext _context;
    private readonly ChampionAnalyticsComputeOptions _options;
    private readonly TieringOptions _tieringOptions;

    public ChampionWinRateComputeService(
        TranscendenceContext context,
        IOptions<ChampionAnalyticsComputeOptions> options,
        IOptions<TieringOptions> tieringOptions)
    {
        _context = context;
        _options = options.Value;
        _tieringOptions = tieringOptions.Value;
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
        var queueFamily = AnalyticsQueueCatalog.Normalize(filter.QueueFamily);
        var hasRoles = AnalyticsQueueCatalog.HasRoles(queueFamily);

        // Base query: Match participants for this champion in this patch
        var baseQuery = _context.MatchParticipants
            .AsNoTracking()
            .Where(mp => mp.ChampionId == championId)
            .OnPatch(patch)
            .FromSuccessfulMatches()
            .InAnalyticsQueue(queueFamily)
            .WithAnalyticsRole(queueFamily);

        // Apply region filter if specified
        baseQuery = baseQuery.InPlatformRegion(filter.Region);

        // Apply role filter if specified
        if (hasRoles && !string.IsNullOrEmpty(filter.Role))
        {
            baseQuery = baseQuery.Where(mp => mp.TeamPosition == filter.Role);
        }

        var participantRanks = from mp in baseQuery
                               join rank in _context.Ranks.AsNoTracking().InAnalyticsRankQueue(queueFamily)
                                   on mp.SummonerId equals rank.SummonerId into rankGroup
                               from soloRank in rankGroup.DefaultIfEmpty()
                               select new
                               {
                                   Role = hasRoles ? mp.TeamPosition! : AnalyticsQueueCatalog.AllRoles,
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
            .GroupBy(pr => new { pr.Role, pr.RankTier })
            .Select(g => new
            {
                g.Key.Role,
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
        var scopedMatchIds = BuildScopedMatchIdQuery(patch, filter.Region, rankTierScope, queueFamily);
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
                    .InAnalyticsQueue(queueFamily)
                    .WithAnalyticsRole(queueFamily)
                    .InPlatformRegion(filter.Region)
                    .Where(mp => !hasRoles || relevantRoles.Contains(mp.TeamPosition!))
                join rank in _context.Ranks.AsNoTracking().InAnalyticsRankQueue(queueFamily)
                    on mp.SummonerId equals rank.SummonerId into rankGroup
                from soloRank in rankGroup.DefaultIfEmpty()
                select new
                {
                    Role = hasRoles ? mp.TeamPosition! : AnalyticsQueueCatalog.AllRoles,
                    mp.ChampionId,
                    mp.Win,
                    RankTier = soloRank != null ? soloRank.Tier : "UNRANKED"
                };

        if (rankTierScope.IsEmeraldPlus)
            populationRanks = populationRanks.Where(pr => RankTierCatalog.EmeraldPlusTiers.Contains(pr.RankTier));
        else if (!string.IsNullOrWhiteSpace(rankTierScope.ExactTier))
            populationRanks = populationRanks.Where(pr => pr.RankTier == rankTierScope.ExactTier);

        var standingsRows = await populationRanks
            .GroupBy(pr => new { pr.Role, pr.RankTier, pr.ChampionId })
            .Select(g => new
            {
                g.Key.Role,
                g.Key.RankTier,
                g.Key.ChampionId,
                Games = g.Count(),
                Wins = g.Sum(x => x.Win ? 1 : 0)
            })
            .ToListAsync(ct);

        // Per (role, tier): champions ranked by win rate (then games, then id) → this champion's rank +
        // population; sum of games → the true pick-rate denominator. Identical to ComputeRoleRankAsync.
        var standingsByRoleTier = standingsRows
            .GroupBy(x => (x.Role, Tier: x.RankTier))
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
    public Task<List<TierListEntry>> ComputeTierListAsync(
        string? role,
        string? rankTier,
        string? region,
        string patch,
        CancellationToken ct) =>
        ComputeTierListAsync(role, rankTier, region, null, patch, ct);

    public async Task<List<TierListEntry>> ComputeTierListAsync(
        string? role,
        string? rankTier,
        string? region,
        string? queueFamily,
        string patch,
        CancellationToken ct)
    {
        var normalizedRole = string.IsNullOrWhiteSpace(role) ? "ALL" : role.ToUpperInvariant();
        var isUnifiedRole = normalizedRole == "ALL";
        var rankTierScope = AnalyticsScopeMath.ParseRankTierScope(rankTier);
        var regionFilter = AnalyticsRegionCatalog.NormalizeToFilter(region);
        var normalizedQueue = AnalyticsQueueCatalog.Normalize(queueFamily);
        var hasRoles = AnalyticsQueueCatalog.HasRoles(normalizedQueue);
        if (!hasRoles)
        {
            normalizedRole = AnalyticsQueueCatalog.AllRoles;
            isUnifiedRole = true;
        }

        // Step 1: Build base query for match participants in this patch
        var baseQuery = _context.MatchParticipants
            .AsNoTracking()
            .OnPatch(patch)
            .FromSuccessfulMatches()
            .InAnalyticsQueue(normalizedQueue)
            .WithAnalyticsRole(normalizedQueue);

        // Apply role filter (if not unified "ALL")
        if (!isUnifiedRole)
        {
            baseQuery = baseQuery.Where(mp => mp.TeamPosition == normalizedRole);
        }

        baseQuery = baseQuery.InPlatformRegion(regionFilter);

        // Only apply rank join semantics when a tier filter is requested.
        // Unfiltered views intentionally keep unranked participants.
        baseQuery = AnalyticsScopeMath.ApplyRankTierScopeToParticipants(
            baseQuery, rankTierScope, _context.Ranks.AsNoTracking(), normalizedQueue);

        var query = baseQuery.Select(mp => new
        {
            mp.ChampionId,
            Role = hasRoles ? mp.TeamPosition! : AnalyticsQueueCatalog.AllRoles,
            mp.Win,
            mp.MatchId
        });
        var totalParticipants = await query.CountAsync(ct);
        if (totalParticipants == 0)
            return [];

        // Keep the scope's distinct match-id set as an IQueryable subquery so the ban rollup stays
        // entirely in SQL (a COUNT subquery + a Contains-subquery), never materialising the (large)
        // id set into app memory. Derived from `query` so the role filter above stays in scope.
        var scopeMatchIds = query.Select(x => x.MatchId).Distinct();
        var totalMatchesInScope = await scopeMatchIds.CountAsync(ct);
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

        // Always aggregate per (champion, role); per-role-first scoring needs role-resolved rows even for
        // the unified ("ALL") request, which the shared scorer then collapses to a primary-role overview.
        var aggregatedChampionStats = await query
            .GroupBy(x => new { x.ChampionId, x.Role })
            .Select(g => new
            {
                g.Key.ChampionId,
                g.Key.Role,
                Games = g.Count(),
                Wins = g.Count(x => x.Win)
            })
            .ToListAsync(ct);

        var aggregated = aggregatedChampionStats
            .Select(x => new ChampionTierScorer.RoleGames(x.ChampionId, x.Role, x.Games, x.Wins))
            .ToList();

        // Raw/live path: previous-patch movement is intentionally omitted (it lives only on the persisted
        // region=ALL grades). Empirical-Bayes shrinkage + absolute cutoffs are applied by the shared scorer.
        return ScoreToEntries(isUnifiedRole, aggregated, banCountsByChampion, totalMatchesInScope);
    }

    // ---- shared scorer plumbing (used by the raw path and the stats fallback) ----

    /// <summary>
    /// Scores a whole scope's per-(champion, role) aggregates through <see cref="ChampionTierScorer"/> and
    /// maps to <see cref="TierListEntry"/> rows: the primary-role overview for a unified request, otherwise
    /// the requested role's rows. Movement is null here (only the persisted region=ALL grades carry it).
    /// </summary>
    private List<TierListEntry> ScoreToEntries(
        bool isUnifiedRole,
        IReadOnlyList<ChampionTierScorer.RoleGames> aggregated,
        IReadOnlyDictionary<int, int> banByChampion,
        int totalMatchesInScope)
    {
        if (aggregated.Count == 0)
            return [];

        var score = ChampionTierScorer.ScoreScope(aggregated, banByChampion, totalMatchesInScope, _tieringOptions);
        var rows = isUnifiedRole ? score.Overview : score.PerRole;
        return rows.Select(s => MapScoredToEntry(s, movement: null, previousTier: null)).ToList();
    }

    private static TierListEntry MapScoredToEntry(
        ChampionTierScorer.ScoredChampion s, TierMovement? movement, TierGrade? previousTier) =>
        new(
            ChampionId: s.ChampionId,
            Role: s.Role,
            Tier: s.Tier,
            WinRate: s.WinRate,
            PickRate: s.PickRate,
            BanRate: s.BanRate,
            Games: s.Games,
            Movement: movement,
            PreviousTier: previousTier,
            StrengthScore: s.StrengthScore,
            ContestedScore: s.ContestedScore,
            RoleBaseline: s.RoleBaseline,
            IsLowSample: s.IsLowSample);

    // Distinct match IDs for the full (all-champion) rank-scoped ranked-solo population in a
    // patch/region — the denominator population for champion-level ban rate. Returned as IQueryable
    // so callers compose CountAsync / Contains in SQL without materialising the (large) id set.
    private IQueryable<Guid> BuildScopedMatchIdQuery(
        string patch,
        string? region,
        AnalyticsScopeMath.RankTierScope rankTierScope,
        string queueFamily)
    {
        var scope = _context.MatchParticipants
            .AsNoTracking()
            .OnPatch(patch)
            .FromSuccessfulMatches()
            .InAnalyticsQueue(queueFamily)
            .WithAnalyticsRole(queueFamily);

        scope = scope.InPlatformRegion(region);

        scope = AnalyticsScopeMath.ApplyRankTierScopeToParticipants(
            scope, rankTierScope, _context.Ranks.AsNoTracking(), queueFamily);

        return scope.Select(mp => mp.MatchId).Distinct();
    }

    public async Task<List<ChampionWinRateDto>> ComputeWinRatesFromStatsAsync(
        int championId,
        ChampionAnalyticsFilter filter,
        string patch,
        CancellationToken ct)
    {
        var queueFamily = AnalyticsQueueCatalog.Normalize(filter.QueueFamily);
        if (!await HasStatsAsync(patch, queueFamily, ct))
            return await ComputeWinRatesAsync(championId, filter, patch, ct);

        var minimumGamesRequired = await AnalyticsSampleThreshold.ResolveAsync(_context, _options, patch, ct);
        var rankTierScope = AnalyticsScopeMath.ParseRankTierScope(filter.RankTier);
        var scopeToken = AnalyticsScopeMath.ScopeTokenOf(rankTierScope);
        var tierFilter = RankTierCatalog.ResolveScopeTiers(scopeToken);
        var region = filter.Region;                                  // already normalized to a platform or null (ALL)
        var roleFilter = AnalyticsQueueCatalog.HasRoles(queueFamily) && !string.IsNullOrEmpty(filter.Role)
            ? filter.Role
            : null;

        // Champion's per-(role, tier) Games/Wins, summed over the platform regions in scope.
        var champQuery = _context.ChampionRoleTierStats.AsNoTracking()
            .Where(x => x.Patch == patch && x.QueueFamily == queueFamily && x.ChampionId == championId);
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
        var banRate = await LookupBanRateAsync(patch, queueFamily, region, scopeToken, championId, ct);

        // Standings (role-rank + pick-rate denominator): every champion in the same (role, tier) scope.
        var relevantRoles = winRateData.Select(x => x.Role).Distinct().ToList();
        var standingsQuery = ApplyStatScope(
            _context.ChampionRoleTierStats.AsNoTracking().Where(x =>
                x.Patch == patch && x.QueueFamily == queueFamily && relevantRoles.Contains(x.Role)),
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

    public Task<List<TierListEntry>> ComputeTierListFromStatsAsync(
        string? role,
        string? rankTier,
        string? region,
        string patch,
        CancellationToken ct) =>
        ComputeTierListFromStatsAsync(role, rankTier, region, null, patch, ct);

    public async Task<List<TierListEntry>> ComputeTierListFromStatsAsync(
        string? role,
        string? rankTier,
        string? region,
        string? queueFamily,
        string patch,
        CancellationToken ct)
    {
        var normalizedQueue = AnalyticsQueueCatalog.Normalize(queueFamily);
        if (!await HasStatsAsync(patch, normalizedQueue, ct))
            return await ComputeTierListAsync(role, rankTier, region, normalizedQueue, patch, ct);

        var normalizedRole = string.IsNullOrWhiteSpace(role) ? "ALL" : role.ToUpperInvariant();
        var isUnifiedRole = normalizedRole == "ALL";
        if (!AnalyticsQueueCatalog.HasRoles(normalizedQueue))
        {
            normalizedRole = AnalyticsQueueCatalog.AllRoles;
            isUnifiedRole = true;
        }
        var rankTierScope = AnalyticsScopeMath.ParseRankTierScope(rankTier);
        var scopeToken = AnalyticsScopeMath.ScopeTokenOf(rankTierScope);
        var tierFilter = RankTierCatalog.ResolveScopeTiers(scopeToken);
        var regionFilter = AnalyticsRegionCatalog.NormalizeToFilter(region);

        // Fast path: the persisted grade table is the single source of truth for the default scopes
        // (region=ALL with scope ALL or EMERALD_PLUS) and the only place patch-over-patch movement exists.
        var isPersistedScope = regionFilter == null
            && (scopeToken == RankTierCatalog.AllScope || scopeToken == RankTierCatalog.EmeraldPlusScope);
        if (isPersistedScope && await HasGradesAsync(patch, normalizedQueue, scopeToken, ct))
            return await ReadGradeTableAsync(patch, normalizedQueue, scopeToken, isUnifiedRole ? "ALL" : normalizedRole, ct);

        // Fallback (specific region or exact tier — or a patch not yet graded): roll the atoms up to the
        // requested scope and score live through the same scorer (no movement).
        var query = ApplyStatScope(
            _context.ChampionRoleTierStats.AsNoTracking().Where(x => x.Patch == patch && x.QueueFamily == normalizedQueue),
            regionFilter, tierFilter);
        if (!isUnifiedRole)
            query = query.Where(x => x.Role == normalizedRole);

        var aggregated = (await query
            .GroupBy(x => new { x.ChampionId, x.Role })
            .Select(g => new { g.Key.ChampionId, g.Key.Role, Games = g.Sum(x => x.Games), Wins = g.Sum(x => x.Wins) })
            .ToListAsync(ct))
            .Select(x => new ChampionTierScorer.RoleGames(x.ChampionId, x.Role, x.Games, x.Wins))
            .ToList();
        if (aggregated.Count == 0)
            return [];

        var regionKey = regionFilter ?? PrecomputedAnalyticsRefresher.AllRegion;
        var totalMatchesInScope = await _context.ScopeMatchCountStats.AsNoTracking()
            .Where(x => x.Patch == patch && x.QueueFamily == normalizedQueue && x.PlatformRegion == regionKey && x.RankScope == scopeToken)
            .Select(x => (int?)x.TotalMatches)
            .FirstOrDefaultAsync(ct) ?? 0;
        var banCountsByChampion = totalMatchesInScope == 0
            ? new Dictionary<int, int>()
            : await _context.ChampionBanScopeStats.AsNoTracking()
                .Where(x => x.Patch == patch && x.QueueFamily == normalizedQueue && x.PlatformRegion == regionKey && x.RankScope == scopeToken)
                .ToDictionaryAsync(x => x.ChampionId, x => x.BannedMatches, ct);

        return ScoreToEntries(isUnifiedRole, aggregated, banCountsByChampion, totalMatchesInScope);
    }

    private Task<bool> HasGradesAsync(string patch, string queueFamily, string scopeToken, CancellationToken ct) =>
        _context.ChampionScopeGradeStats.AsNoTracking()
            .AnyAsync(x => x.Patch == patch
                && x.QueueFamily == queueFamily
                && x.PlatformRegion == PrecomputedAnalyticsRefresher.AllRegion
                && x.RankScope == scopeToken, ct);

    private async Task<List<TierListEntry>> ReadGradeTableAsync(
        string patch, string queueFamily, string scopeToken, string roleKey, CancellationToken ct)
    {
        var rows = await _context.ChampionScopeGradeStats.AsNoTracking()
            .Where(x => x.Patch == patch
                && x.QueueFamily == queueFamily
                && x.PlatformRegion == PrecomputedAnalyticsRefresher.AllRegion
                && x.RankScope == scopeToken
                && x.Role == roleKey)
            .ToListAsync(ct);

        // Match the scorer's ordering (strength desc, games desc, championId asc).
        return rows
            .OrderByDescending(x => x.StrengthScore)
            .ThenByDescending(x => x.Games)
            .ThenBy(x => x.ChampionId)
            .Select(x => new TierListEntry(
                ChampionId: x.ChampionId,
                Role: x.PrimaryRole,
                Tier: (TierGrade)x.Tier,
                WinRate: x.WinRate,
                PickRate: x.PickRate,
                BanRate: x.BanRate,
                Games: x.Games,
                Movement: x.Movement.HasValue ? (TierMovement)x.Movement.Value : null,
                PreviousTier: x.PreviousTier.HasValue ? (TierGrade)x.PreviousTier.Value : null,
                StrengthScore: x.StrengthScore,
                ContestedScore: x.ContestedScore,
                RoleBaseline: x.RoleBaseline,
                IsLowSample: x.IsLowSample))
            .ToList();
    }

    public Task<DateTime?> GetAnalyticsComputedAtAsync(string patch, string? queueFamily, CancellationToken ct)
    {
        var normalizedQueue = AnalyticsQueueCatalog.Normalize(queueFamily);
        return
        _context.ChampionRoleTierStats
            .AsNoTracking()
            .Where(x => x.Patch == patch && x.QueueFamily == normalizedQueue)
            .OrderByDescending(x => x.ComputedAtUtc)
            .Select(x => (DateTime?)x.ComputedAtUtc)
            .FirstOrDefaultAsync(ct);
    }

    public Task<DateTime?> GetAnalyticsComputedAtAsync(string patch, CancellationToken ct) =>
        GetAnalyticsComputedAtAsync(patch, null, ct);

    // ---- shared helpers for the stats path ----

    private Task<bool> HasStatsAsync(string patch, string queueFamily, CancellationToken ct) =>
        _context.ChampionRoleTierStats.AsNoTracking()
            .AnyAsync(x => x.Patch == patch && x.QueueFamily == queueFamily, ct);

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
        string patch, string queueFamily, string? region, string scopeToken, int championId, CancellationToken ct)
    {
        var regionKey = region ?? PrecomputedAnalyticsRefresher.AllRegion;
        var totalMatchesInScope = await _context.ScopeMatchCountStats.AsNoTracking()
            .Where(x => x.Patch == patch && x.QueueFamily == queueFamily && x.PlatformRegion == regionKey && x.RankScope == scopeToken)
            .Select(x => (int?)x.TotalMatches)
            .FirstOrDefaultAsync(ct) ?? 0;
        if (totalMatchesInScope == 0)
            return 0.0;

        var bannedMatches = await _context.ChampionBanScopeStats.AsNoTracking()
            .Where(x => x.Patch == patch && x.QueueFamily == queueFamily && x.PlatformRegion == regionKey && x.RankScope == scopeToken && x.ChampionId == championId)
            .Select(x => (int?)x.BannedMatches)
            .FirstOrDefaultAsync(ct) ?? 0;
        return (double)bannedMatches / totalMatchesInScope;
    }
}
