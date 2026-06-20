using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Transcendence.Data;
using Transcendence.Data.Models.LoL.Match;
using Transcendence.Service.Core.Services.Analytics.Interfaces;
using Transcendence.Service.Core.Services.Analytics.Models;
using Transcendence.Service.Core.Services.RiotApi;

namespace Transcendence.Service.Core.Services.Analytics.Implementations;

/// <summary>
/// Raw computation service for champion analytics using EF Core aggregation.
/// </summary>
public class ChampionAnalyticsComputeService : IChampionAnalyticsComputeService
{
    private const int MinMatchupSampleSize = 30;
    private const int MatchupsToShow = 5;
    private readonly TranscendenceContext _context;
    private readonly ChampionAnalyticsComputeOptions _options;
    private readonly ILogger<ChampionAnalyticsComputeService> _logger;

    public ChampionAnalyticsComputeService(
        TranscendenceContext context,
        IOptions<ChampionAnalyticsComputeOptions> options,
        ILogger<ChampionAnalyticsComputeService> logger)
    {
        _context = context;
        _options = options.Value;
        _logger = logger;
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
        var minimumGamesRequired = await GetAdaptiveMinimumGamesRequiredAsync(patch, ct);
        var rankTierScope = ParseRankTierScope(filter.RankTier);

        // Base query: Match participants for this champion in this patch
        var baseQuery = _context.MatchParticipants
            .AsNoTracking()
            .Where(mp => mp.ChampionId == championId)
            .Where(mp => mp.Match.Patch == patch)
            .Where(mp => mp.Match.Status == FetchStatus.Success)
            .Where(mp => mp.Match.QueueId == QueueCatalog.RankedSoloDuoQueueId ||
                         (mp.Match.QueueId == 0 &&
                          mp.Match.QueueType == QueueCatalog.RankedSoloDuoQueueId.ToString()))
            .Where(mp => mp.TeamPosition != null && mp.TeamPosition != "");

        // Apply region filter if specified
        if (!string.IsNullOrEmpty(filter.Region))
        {
            baseQuery = baseQuery.Where(mp => mp.Summoner.PlatformRegion == filter.Region);
        }

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
                .Where(pr =>
                    pr.RankTier == "EMERALD" ||
                    pr.RankTier == "DIAMOND" ||
                    pr.RankTier == "MASTER" ||
                    pr.RankTier == "GRANDMASTER" ||
                    pr.RankTier == "CHALLENGER");
        }
        else if (!string.IsNullOrWhiteSpace(rankTierScope.ExactTier))
        {
            participantRanks = participantRanks
                .Where(pr => pr.RankTier == rankTierScope.ExactTier);
        }

        var participantRankRows = await participantRanks.ToListAsync(ct);
        var totalGames = participantRankRows.Count;
        if (totalGames == 0)
            return [];

        var effectiveMinimumGames = ResolveEffectiveSampleSize(minimumGamesRequired, totalGames, floor: 3);

        // Group by role and rank tier, calculate win rates
        var groupedData = participantRankRows
            .GroupBy(pr => new { pr.TeamPosition, pr.RankTier })
            .Select(g => new
            {
                Role = g.Key.TeamPosition!,
                RankTier = g.Key.RankTier,
                Games = g.Count(),
                Wins = g.Sum(pr => pr.Win ? 1 : 0),
                MatchIds = g.Select(pr => pr.MatchId).Distinct().ToList()
            })
            .ToList();

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

        // Convert to DTOs
        var scopedMatchIds = participantRankRows
            .Select(pr => pr.MatchId)
            .Distinct()
            .ToList();
        var bannedMatchIds = scopedMatchIds.Count == 0
            ? new HashSet<Guid>()
            : (await _context.MatchBans
                    .AsNoTracking()
                    .Where(b => b.ChampionId == championId && scopedMatchIds.Contains(b.MatchId))
                    .Select(b => b.MatchId)
                    .Distinct()
                    .ToListAsync(ct))
                .ToHashSet();

        var result = new List<ChampionWinRateDto>(winRateData.Count);
        foreach (var data in winRateData)
        {
            var rowBanCount = data.MatchIds.Count(matchId => bannedMatchIds.Contains(matchId));
            var roleRank = await ComputeRoleRankAsync(
                championId,
                data.Role,
                data.RankTier,
                patch,
                filter.Region,
                ct);

            result.Add(new ChampionWinRateDto(
                ChampionId: championId,
                Role: data.Role,
                RankTier: data.RankTier,
                Games: data.Games,
                Wins: data.Wins,
                WinRate: data.Games > 0 ? (double)data.Wins / data.Games : 0.0,
                PickRate: totalGames > 0 ? (double)data.Games / totalGames : 0.0,
                BanRate: data.MatchIds.Count > 0 ? (double)rowBanCount / data.MatchIds.Count : 0.0,
                RoleRank: roleRank.RoleRank,
                RolePopulation: roleRank.RolePopulation,
                Patch: patch
            ));
        }

        result = result
            .OrderByDescending(x => x.Games)
            .ToList();

        return result;
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
        var rankTierScope = ParseRankTierScope(rankTier);
        var minimumGamesRequired = await GetAdaptiveMinimumGamesRequiredAsync(patch, ct);
        var regionFilter = AnalyticsRegionCatalog.NormalizeToFilter(region);

        // Step 1: Build base query for match participants in this patch
        var baseQuery = _context.MatchParticipants
            .AsNoTracking()
            .Where(mp => mp.Match.Patch == patch)
            .Where(mp => mp.Match.Status == FetchStatus.Success)
            .Where(mp => mp.Match.QueueId == QueueCatalog.RankedSoloDuoQueueId ||
                         (mp.Match.QueueId == 0 &&
                          mp.Match.QueueType == QueueCatalog.RankedSoloDuoQueueId.ToString()))
            .Where(mp => mp.TeamPosition != null && mp.TeamPosition != "");

        // Apply role filter (if not unified "ALL")
        if (!isUnifiedRole)
        {
            baseQuery = baseQuery.Where(mp => mp.TeamPosition == normalizedRole);
        }

        if (!string.IsNullOrWhiteSpace(regionFilter))
        {
            baseQuery = baseQuery.Where(mp => mp.Summoner.PlatformRegion == regionFilter);
        }

        // Only apply rank join semantics when a tier filter is requested.
        // Unfiltered views intentionally keep unranked participants.
        baseQuery = ApplyRankTierScopeToParticipants(baseQuery, rankTierScope, _context.Ranks.AsNoTracking());

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

        var effectiveMinimumGames = ResolveEffectiveSampleSize(minimumGamesRequired, totalParticipants, floor: 5);

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
            ConservativeWinRate = ComputeWilsonLowerBound(c.Wins, c.Games),
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
        .OrderByDescending(x => x.CompositeScore)
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

    private async Task<int> GetAdaptiveMinimumGamesRequiredAsync(string patch, CancellationToken ct)
    {
        var steadyStateMinimum = Math.Max(1, _options.MinimumGamesRequired);

        var releaseDate = await _context.Patches
            .AsNoTracking()
            .Where(p => p.Version == patch)
            .Select(p => (DateTime?)p.ReleaseDate)
            .FirstOrDefaultAsync(ct);

        if (!releaseDate.HasValue)
            return steadyStateMinimum;

        var releaseUtc = releaseDate.Value.Kind == DateTimeKind.Utc
            ? releaseDate.Value
            : DateTime.SpecifyKind(releaseDate.Value, DateTimeKind.Utc);
        var patchAgeHours = Math.Max(0, (DateTime.UtcNow - releaseUtc).TotalHours);
        var patchPhase = AnalyticsPatchPhaseCalculator.Resolve(patchAgeHours, _options);
        return AnalyticsPatchPhaseCalculator.RecommendedSampleSize(patchPhase, _options);
    }

    private static RankTierScope ParseRankTierScope(string? rankTier)
    {
        if (string.IsNullOrWhiteSpace(rankTier))
            return new RankTierScope("all", null, false);

        var normalized = rankTier.Trim().ToUpperInvariant().Replace("+", "_PLUS");
        if (normalized == "ALL")
            return new RankTierScope("all", null, false);

        if (normalized == "EMERALD_PLUS")
            return new RankTierScope("EMERALD_PLUS", null, true);

        return new RankTierScope(normalized, normalized, false);
    }

    private static IQueryable<Data.Models.LoL.Match.MatchParticipant> ApplyRankTierScopeToParticipants(
        IQueryable<Data.Models.LoL.Match.MatchParticipant> query,
        RankTierScope scope,
        IQueryable<Data.Models.LoL.Account.Rank> ranks)
    {
        if (!scope.HasFilter)
            return query;

        if (scope.IsEmeraldPlus)
        {
            return query.Where(mp => ranks.Any(r =>
                r.QueueType == "RANKED_SOLO_5x5" &&
                r.SummonerId == mp.SummonerId &&
                (r.Tier == "EMERALD" ||
                 r.Tier == "DIAMOND" ||
                 r.Tier == "MASTER" ||
                 r.Tier == "GRANDMASTER" ||
                 r.Tier == "CHALLENGER")));
        }

        return query.Where(mp => ranks.Any(r =>
            r.QueueType == "RANKED_SOLO_5x5" &&
            r.SummonerId == mp.SummonerId &&
            r.Tier == scope.ExactTier));
    }

    private static HashSet<string> ResolvePlatformsForRegion(string region)
    {
        return region switch
        {
            "NA" => ["NA1"],
            "EUW" => ["EUW1"],
            "KR" => ["KR"],
            "CN" => ["CN1", "CN2"],
            "ALL" => [],
            _ => [region]
        };
    }

    private async Task<(int? RoleRank, int? RolePopulation)> ComputeRoleRankAsync(
        int championId,
        string role,
        string rankTier,
        string patch,
        string? region,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(role) || string.Equals(rankTier, "UNRANKED", StringComparison.OrdinalIgnoreCase))
            return (null, null);

        var roleQuery = _context.MatchParticipants
            .AsNoTracking()
            .Where(mp => mp.Match.Patch == patch)
            .Where(mp => mp.Match.Status == FetchStatus.Success)
            .Where(mp => mp.Match.QueueId == QueueCatalog.RankedSoloDuoQueueId ||
                         (mp.Match.QueueId == 0 &&
                          mp.Match.QueueType == QueueCatalog.RankedSoloDuoQueueId.ToString()))
            .Where(mp => mp.TeamPosition == role);

        if (!string.IsNullOrWhiteSpace(region))
            roleQuery = roleQuery.Where(mp => mp.Summoner.PlatformRegion == region);

        roleQuery = roleQuery.Where(mp => _context.Ranks.Any(r =>
            r.QueueType == "RANKED_SOLO_5x5" &&
            r.SummonerId == mp.SummonerId &&
            r.Tier == rankTier));

        var standings = await roleQuery
            .GroupBy(mp => mp.ChampionId)
            .Select(g => new
            {
                ChampionId = g.Key,
                Games = g.Count(),
                WinRate = g.Count() > 0 ? (double)g.Count(x => x.Win) / g.Count() : 0.0
            })
            .OrderByDescending(x => x.WinRate)
            .ThenByDescending(x => x.Games)
            .ThenBy(x => x.ChampionId)
            .ToListAsync(ct);

        if (standings.Count == 0)
            return (null, null);

        var rolePopulation = standings.Count;
        var rank = standings.FindIndex(s => s.ChampionId == championId);
        return rank >= 0 ? (rank + 1, rolePopulation) : (null, rolePopulation);
    }

    private static int ResolveEffectiveSampleSize(int configuredMinimum, int availableGames, int floor)
    {
        if (availableGames <= 0)
            return int.MaxValue;

        var safeConfiguredMinimum = Math.Max(1, configuredMinimum);
        var safeFloor = Math.Max(1, floor);
        var proportionalMinimum = (int)Math.Ceiling(availableGames * 0.15);
        var boundedFloor = Math.Min(availableGames, Math.Max(safeFloor, proportionalMinimum));
        return Math.Max(1, Math.Min(safeConfiguredMinimum, boundedFloor));
    }

    private static double ComputeWilsonLowerBound(int wins, int games, double z = 1.96)
    {
        if (games <= 0)
            return 0.0;

        var p = (double)wins / games;
        var zSquared = z * z;
        var denominator = 1 + zSquared / games;
        var center = p + zSquared / (2 * games);
        var margin = z * Math.Sqrt((p * (1 - p) + zSquared / (4 * games)) / games);
        return Math.Max(0.0, (center - margin) / denominator);
    }

    private const double CoreItemThreshold = 0.70;
    private const int MinBuildSampleSize = 30;
    private const double ItemMetadataCoverageFallbackThreshold = 0.90;
    private const int CoreBuildPathSlots = 3;
    private const int MaxBuildPathSlots = 6;
    // Top builds are grouped on the first N core legendaries rather than the full 6-item set so a
    // single situational-slot difference does not fragment otherwise-identical builds below the
    // sample floor. The completed-item set is sorted ascending, so we cannot recover purchase order
    // here — we use the global-core membership (items appearing in >= CoreItemThreshold of games) as
    // the core signal, which is the same core notion surfaced by globalCoreItems.
    private const int BuildGroupingCoreSlots = 4;
    private static readonly IReadOnlyDictionary<int, BuildItemMetadata> EmptyItemMetadata =
        new Dictionary<int, BuildItemMetadata>();

    /// <summary>
    /// Computes top 3 builds for a champion with items and runes bundled.
    /// Core items (70%+ appearance) distinguished from situational.
    /// </summary>
    public async Task<ChampionBuildsResponse> ComputeBuildsAsync(
        int championId,
        string role,
        string? rankTier,
        string? region,
        string patch,
        CancellationToken ct)
    {
        var minimumGamesRequired = await GetAdaptiveMinimumGamesRequiredAsync(patch, ct);
        var rankTierScope = ParseRankTierScope(rankTier);
        var normalizedRegion = AnalyticsRegionCatalog.NormalizeOrDefault(region);
        var regionFilter = AnalyticsRegionCatalog.NormalizeToFilter(region);

        // Step 1: Get all match data for this champion/role/patch/tier with items and runes
        var baseQuery = _context.MatchParticipants
            .AsNoTracking()
            .AsSplitQuery()
            .Include(mp => mp.Items)
            .Include(mp => mp.Runes)
            .Where(mp => mp.ChampionId == championId
                      && mp.Match.Patch == patch
                      && mp.Match.Status == FetchStatus.Success
                      && (mp.Match.QueueId == QueueCatalog.RankedSoloDuoQueueId ||
                          (mp.Match.QueueId == 0 &&
                           mp.Match.QueueType == QueueCatalog.RankedSoloDuoQueueId.ToString()))
                      && mp.TeamPosition == role);

        if (!string.IsNullOrWhiteSpace(regionFilter))
        {
            baseQuery = baseQuery.Where(mp => mp.Summoner.PlatformRegion == regionFilter);
        }

        baseQuery = ApplyRankTierScopeToParticipants(baseQuery, rankTierScope, _context.Ranks.AsNoTracking());

        var matchData = await baseQuery
            .Select(mp => new
            {
                mp.Win,
                MatchGuid = mp.Match.Id,
                mp.ParticipantId,
                mp.SummonerSpell1Id,
                mp.SummonerSpell2Id,
                Items = mp.Items.Select(i => i.ItemId).ToList(),
                Runes = mp.Runes.Select(r => new StoredRuneSelection(
                    r.RuneId,
                    r.SelectionTree,
                    r.SelectionIndex,
                    r.StyleId)).ToList()
            })
            .ToListAsync(ct);

        var allItemIds = matchData
            .SelectMany(m => m.Items)
            .Where(itemId => itemId != 0)
            .Distinct()
            .ToList();

        var itemMetadataById = allItemIds.Count == 0
            ? new Dictionary<int, BuildItemMetadata>()
            : await _context.ItemVersions
                .AsNoTracking()
                .Where(iv => iv.PatchVersion == patch && allItemIds.Contains(iv.ItemId))
                .Select(iv => new
                {
                    iv.ItemId,
                    iv.BuildsFrom,
                    iv.BuildsInto,
                    iv.Tags,
                    iv.InStore,
                    iv.PriceTotal
                })
                .ToDictionaryAsync(
                    iv => iv.ItemId,
                    iv => new BuildItemMetadata(
                        iv.BuildsFrom,
                        iv.BuildsInto,
                        iv.Tags,
                        iv.InStore,
                        iv.PriceTotal),
                    ct);

        var itemMetadataCoverage = allItemIds.Count == 0
            ? 1.0
            : (double)itemMetadataById.Count / allItemIds.Count;
        var useLegacyFallback = itemMetadataCoverage < ItemMetadataCoverageFallbackThreshold;

        if (allItemIds.Count > 0 && itemMetadataById.Count == 0)
        {
            _logger.LogWarning(
                "No item metadata found for patch {Patch} while computing builds for champion {ChampionId}/{Role}. Using legacy build-item fallback.",
                patch,
                championId,
                role);
        }
        else if (useLegacyFallback)
        {
            _logger.LogWarning(
                "Item metadata coverage is {Coverage:P1} for patch {Patch} while computing builds for champion {ChampionId}/{Role}. Using legacy build-item fallback.",
                itemMetadataCoverage,
                patch,
                championId,
                role);
        }

        var buildEligibleMatches = matchData
            .Select(m => new
            {
                m.Win,
                Runes = m.Runes,
                Items = NormalizeCompletedBuildItems(m.Items, itemMetadataById, useLegacyFallback)
            })
            .Where(m => m.Items.Count > 0)
            .ToList();

        var effectiveMinimumGames = ResolveEffectiveSampleSize(minimumGamesRequired, buildEligibleMatches.Count, floor: 3);
        if (buildEligibleMatches.Count < effectiveMinimumGames)
            return new ChampionBuildsResponse(championId, role, rankTierScope.CacheToken, normalizedRegion, patch,
                new List<int>(), new List<ChampionBuildDto>());

        // Step 2: Calculate global core items from completed build-impact items.
        var totalGames = buildEligibleMatches.Count;
        var itemFrequency = buildEligibleMatches
            .SelectMany(m => m.Items.Distinct())
            .GroupBy(itemId => itemId)
            .ToDictionary(
                g => g.Key,
                g => (double)g.Count() / totalGames
            );

        var globalCoreItems = itemFrequency
            .Where(kvp => kvp.Value >= CoreItemThreshold)
            .Select(kvp => kvp.Key)
            .ToList();

        // Step 3: Get rune metadata for style determination
        var allRuneIds = buildEligibleMatches.SelectMany(m => m.Runes.Select(r => r.RuneId)).Distinct().ToList();
        var runeMetadata = await _context.RuneVersions
            .AsNoTracking()
            .Where(rv => allRuneIds.Contains(rv.RuneId) && rv.PatchVersion == patch)
            .Select(rv => new { rv.RuneId, rv.RunePathId, rv.Slot })
            .ToDictionaryAsync(rv => rv.RuneId, rv => new RuneMetadata(rv.RunePathId, rv.Slot), ct);

        // Step 4: Group by build (items + runes as key)
        var effectiveBuildSampleSize = ResolveEffectiveSampleSize(MinBuildSampleSize, totalGames, floor: 2);
        var buildGroups = buildEligibleMatches
            .Select(m => new
            {
                m.Win,
                // Group on the first N core legendaries, not the full 6-item set (see BuildGroupingKey).
                ItemKey = BuildGroupingKey(m.Items, globalCoreItems),
                // Build rune structure
                RuneInfo = BuildRuneInfo(m.Runes, runeMetadata),
                Items = m.Items
            })
            .GroupBy(m => new { m.ItemKey, m.RuneInfo.Key })
            .Select(g => new
            {
                Items = g.First().Items,
                RuneInfo = g.First().RuneInfo,
                Games = g.Count(),
                Wins = g.Sum(x => x.Win ? 1 : 0),
                WinRate = (double)g.Sum(x => x.Win ? 1 : 0) / g.Count()
            })
            .Where(b => b.Games >= effectiveBuildSampleSize)
            .OrderByDescending(b => b.Games * b.WinRate) // Score: popularity * success
            .Take(3)
            .ToList();

        if (buildGroups.Count == 0)
        {
            buildGroups = buildEligibleMatches
                .Select(m => new
                {
                    m.Win,
                    ItemKey = BuildGroupingKey(m.Items, globalCoreItems),
                    RuneInfo = BuildRuneInfo(m.Runes, runeMetadata),
                    Items = m.Items
                })
                .GroupBy(m => new { m.ItemKey, m.RuneInfo.Key })
                .Select(g => new
                {
                    Items = g.First().Items,
                    RuneInfo = g.First().RuneInfo,
                    Games = g.Count(),
                    Wins = g.Sum(x => x.Win ? 1 : 0),
                    WinRate = (double)g.Sum(x => x.Win ? 1 : 0) / g.Count()
                })
                .Where(b => b.Games >= 1)
                .OrderByDescending(b => b.Games * b.WinRate)
                .Take(3)
                .ToList();
        }

        // Step 5: Map to DTOs
        var builds = buildGroups.Select(build => new ChampionBuildDto(
            build.Items,
            globalCoreItems,
            build.Items.Where(i => !globalCoreItems.Contains(i)).ToList(),
            build.RuneInfo.PrimaryStyleId,
            build.RuneInfo.SubStyleId,
            build.RuneInfo.PrimaryRunes,
            build.RuneInfo.SubRunes,
            build.RuneInfo.StatShards,
            build.Games,
            build.WinRate
        )).ToList();

        // Sectioned, timing-aware build path (spells, skill order, starters, boots, ordered core
        // with completion timing, and 4th/5th/6th situational) from the same participants' timeline data.
        // baseQuery is reused so the timeline fetches join on (MatchId, ParticipantId) and read only
        // this champion's purchase/skill rows, not all 10 participants per match.
        var sections = await ComputeBuildPathSectionsAsync(
            baseQuery,
            matchData.Select(m => new BuildPathParticipantInput(
                m.MatchGuid, m.ParticipantId, m.Win, m.SummonerSpell1Id, m.SummonerSpell2Id)).ToList(),
            ct);

        return new ChampionBuildsResponse(
            championId,
            role,
            rankTierScope.CacheToken,
            normalizedRegion,
            patch,
            globalCoreItems,
            builds,
            SummonerSpells: sections.SummonerSpells,
            SkillOrder: sections.SkillOrder,
            StartingItems: sections.StartingItems,
            Boots: sections.Boots,
            CoreBuildPath: sections.CoreBuildPath,
            SituationalSlots: sections.SituationalSlots
        );
    }

    public async Task<ChampionProBuildsResponse> ComputeProBuildsAsync(
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

        var proQuery = _context.TrackedProSummoners
            .AsNoTracking()
            .Where(x => x.IsActive);

        proQuery = normalizedScope switch
        {
            "highelo" => proQuery.Where(x => x.IsHighEloOtp),
            "all" => proQuery.Where(x => x.IsPro || x.IsHighEloOtp),
            _ => proQuery.Where(x => x.IsPro)
        };

        if (!string.Equals(normalizedRegion, "ALL", StringComparison.Ordinal))
        {
            var platforms = ResolvePlatformsForRegion(normalizedRegion);
            proQuery = proQuery.Where(x => platforms.Contains(x.PlatformRegion.ToUpper()));
        }

        var proRoster = await proQuery
            .Select(x => new
            {
                x.Puuid,
                x.PlatformRegion,
                x.GameName,
                x.TagLine,
                x.ProName,
                x.TeamName
            })
            .ToListAsync(ct);

        var trackedPuuids = proRoster
            .Select(x => x.Puuid)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (trackedPuuids.Count == 0)
            return new ChampionProBuildsResponse(championId, patch, normalizedRole, normalizedRegion, normalizedScope, [], [], []);

        var participantQuery = _context.MatchParticipants
            .AsNoTracking()
            .AsSplitQuery()
            .Include(mp => mp.Items)
            .Include(mp => mp.Runes)
            .Include(mp => mp.Summoner)
            .Where(mp => mp.ChampionId == championId)
            .Where(mp => mp.Match.Patch == patch)
            .Where(mp => mp.Match.Status == FetchStatus.Success)
            .Where(mp => mp.Match.QueueId == QueueCatalog.RankedSoloDuoQueueId ||
                         (mp.Match.QueueId == 0 &&
                          mp.Match.QueueType == QueueCatalog.RankedSoloDuoQueueId.ToString()))
            .Where(mp => mp.Puuid != null && trackedPuuids.Contains(mp.Puuid));

        if (!string.Equals(normalizedRole, "ALL", StringComparison.Ordinal))
            participantQuery = participantQuery.Where(mp => mp.TeamPosition == normalizedRole);

        // Bound the heavy item/rune collection projection to the most-recent N rows so the wide
        // role=ALL + scope=all + region=ALL pool cannot command-timeout (the surface only renders
        // recent matches + aggregate top-players/common-builds, which a recency window represents).
        var maxParticipantRows = Math.Max(100, _options.ProBuildMaxParticipantRows);

        var rows = await participantQuery
            .OrderByDescending(mp => mp.Match.MatchDate)
            .ThenByDescending(mp => mp.Match.MatchId)
            .Take(maxParticipantRows)
            .Select(mp => new
            {
                mp.Match.MatchId,
                MatchGuid = mp.Match.Id,
                mp.Match.MatchDate,
                mp.Win,
                mp.ParticipantId,
                mp.SummonerSpell1Id,
                mp.SummonerSpell2Id,
                mp.Puuid,
                mp.Summoner.GameName,
                mp.Summoner.TagLine,
                Items = mp.Items.Select(i => i.ItemId).ToList(),
                Runes = mp.Runes.Select(r => new StoredRuneSelection(
                    r.RuneId,
                    r.SelectionTree,
                    r.SelectionIndex,
                    r.StyleId)).ToList()
            })
            .ToListAsync(ct);

        if (rows.Count == 0)
            return new ChampionProBuildsResponse(championId, patch, normalizedRole, normalizedRegion, normalizedScope, [], [], []);

        var rosterByPuuid = proRoster
            .GroupBy(x => x.Puuid, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var allRuneIds = rows
            .SelectMany(r => r.Runes.Select(x => x.RuneId))
            .Distinct()
            .ToList();

        var runeMetadata = await _context.RuneVersions
            .AsNoTracking()
            .Where(rv => allRuneIds.Contains(rv.RuneId) && rv.PatchVersion == patch)
            .Select(rv => new { rv.RuneId, rv.RunePathId, rv.Slot })
            .ToDictionaryAsync(rv => rv.RuneId, rv => new RuneMetadata(rv.RunePathId, rv.Slot), ct);

        // Ordered build path + skill orders for the projected pro matches (timeline-derived).
        var proMatchGuids = rows.Select(r => r.MatchGuid).Distinct().ToList();

        var proPurchasesByParticipant = (await _context.MatchParticipantItemPurchases
                .AsNoTracking()
                .Where(p => proMatchGuids.Contains(p.MatchId))
                .Select(p => new { p.MatchId, p.ParticipantId, p.PurchaseIndex, p.ItemId, p.Category })
                .ToListAsync(ct))
            .GroupBy(p => (p.MatchId, p.ParticipantId))
            .ToDictionary(
                g => g.Key,
                g => g.Where(x => x.Category != BuildItemCategory.Starter)
                    .OrderBy(x => x.PurchaseIndex)
                    .Select(x => x.ItemId)
                    .ToList());

        var proSkillByParticipant = (await _context.MatchParticipantSkillOrders
                .AsNoTracking()
                .Where(s => proMatchGuids.Contains(s.MatchId))
                .Select(s => new { s.MatchId, s.ParticipantId, s.FirstThree, s.MaxOrder })
                .ToListAsync(ct))
            .GroupBy(s => (s.MatchId, s.ParticipantId))
            .ToDictionary(g => g.Key, g => g.First());

        var projectedRows = rows
            .Select(r =>
            {
                var runeInfo = BuildRuneInfo(r.Runes, runeMetadata);
                rosterByPuuid.TryGetValue(r.Puuid ?? string.Empty, out var roster);
                var playerName = !string.IsNullOrWhiteSpace(roster?.ProName)
                    ? roster.ProName
                    : (r.GameName != null && r.TagLine != null ? $"{r.GameName}#{r.TagLine}" : r.GameName);

                // Covered rows use the cleaned, purchase-ordered path; uncovered rows fall back to the
                // raw inventory cleaned through the same completed-item filter (legacy exclusions) so
                // both branches yield comparable item sets and don't fragment commonBuilds grouping
                // during the timeline-backfill window.
                var orderedItems =
                    proPurchasesByParticipant.TryGetValue((r.MatchGuid, r.ParticipantId), out var purchasePath) && purchasePath.Count > 0
                        ? purchasePath
                        : NormalizeCompletedBuildItems(r.Items, EmptyItemMetadata, useLegacyFallback: true);

                proSkillByParticipant.TryGetValue((r.MatchGuid, r.ParticipantId), out var skill);

                return new
                {
                    r.MatchId,
                    r.MatchDate,
                    r.Win,
                    PlayerName = playerName,
                    TeamName = roster?.TeamName,
                    Items = orderedItems,
                    Spell1Id = r.SummonerSpell1Id,
                    Spell2Id = r.SummonerSpell2Id,
                    SkillOrder = skill is not null ? new SkillOrderDto(skill.FirstThree, skill.MaxOrder) : null,
                    RuneInfo = runeInfo
                };
            })
            .ToList();

        var recentMatches = projectedRows
            .OrderByDescending(r => r.MatchDate)
            .ThenByDescending(r => r.MatchId)
            .Take(25)
            .Select(r => new ProMatchBuildDto(
                r.MatchId ?? string.Empty,
                r.PlayerName,
                r.TeamName,
                r.Win,
                r.MatchDate,
                r.Items,
                r.RuneInfo.PrimaryStyleId,
                r.RuneInfo.SubStyleId,
                r.RuneInfo.PrimaryRunes,
                r.RuneInfo.SubRunes,
                r.RuneInfo.StatShards,
                r.Spell1Id,
                r.Spell2Id,
                r.SkillOrder))
            .ToList();

        var topPlayers = projectedRows
            .GroupBy(r => new { r.PlayerName, r.TeamName })
            .Select(g => new ProPlayerSummaryDto(
                g.Key.PlayerName,
                g.Key.TeamName,
                g.Count(),
                g.Count() > 0 ? (double)g.Count(x => x.Win) / g.Count() : 0.0))
            .OrderByDescending(p => p.Games)
            .ThenByDescending(p => p.WinRate)
            .Take(10)
            .ToList();

        // Group by the item set (sorted key) for stable grouping, but display a representative
        // member's purchase-ordered items.
        var commonBuilds = projectedRows
            .GroupBy(r => string.Join(",", r.Items.OrderBy(i => i)))
            .Select(g => new CommonProBuildDto(
                g.First().Items,
                g.Count(),
                g.Count() > 0 ? (double)g.Count(x => x.Win) / g.Count() : 0.0))
            .OrderByDescending(x => x.Games)
            .ThenByDescending(x => x.WinRate)
            .Take(10)
            .ToList();

        return new ChampionProBuildsResponse(
            championId,
            patch,
            normalizedRole,
            normalizedRegion,
            normalizedScope,
            recentMatches,
            topPlayers,
            commonBuilds);
    }

    public async Task<ProChampionPlayrateResponse> ComputeProChampionPlayrateAsync(
        string? region,
        string scope,
        string patch,
        CancellationToken ct)
    {
        var normalizedRegion = string.IsNullOrWhiteSpace(region) ? "ALL" : region.Trim().ToUpperInvariant();
        var normalizedScope = NormalizeProScope(scope);

        var rosterQuery = _context.TrackedProSummoners
            .AsNoTracking()
            .Where(x => x.IsActive);

        rosterQuery = normalizedScope switch
        {
            "highelo" => rosterQuery.Where(x => x.IsHighEloOtp),
            "all" => rosterQuery.Where(x => x.IsPro || x.IsHighEloOtp),
            _ => rosterQuery.Where(x => x.IsPro)
        };

        if (!string.Equals(normalizedRegion, "ALL", StringComparison.Ordinal))
        {
            var platforms = ResolvePlatformsForRegion(normalizedRegion);
            rosterQuery = rosterQuery.Where(x => platforms.Contains(x.PlatformRegion.ToUpper()));
        }

        var rosterPuuids = await rosterQuery
            .Select(x => x.Puuid)
            .ToListAsync(ct);

        var trackedPuuids = rosterPuuids
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (trackedPuuids.Count == 0)
            return new ProChampionPlayrateResponse(patch, normalizedRegion, normalizedScope, []);

        var rows = await _context.MatchParticipants
            .AsNoTracking()
            .Where(mp => mp.Match.Patch == patch)
            .Where(mp => mp.Match.Status == FetchStatus.Success)
            .Where(mp => mp.Match.QueueId == QueueCatalog.RankedSoloDuoQueueId ||
                         (mp.Match.QueueId == 0 &&
                          mp.Match.QueueType == QueueCatalog.RankedSoloDuoQueueId.ToString()))
            .Where(mp => mp.Puuid != null && trackedPuuids.Contains(mp.Puuid))
            .Select(mp => new { mp.ChampionId, mp.Win, mp.Puuid })
            .ToListAsync(ct);

        if (rows.Count == 0)
            return new ProChampionPlayrateResponse(patch, normalizedRegion, normalizedScope, []);

        var champions = rows
            .GroupBy(r => r.ChampionId)
            .Select(g =>
            {
                var games = g.Count();
                var wins = g.Count(x => x.Win);
                return new ProChampionPlayrateDto(
                    g.Key,
                    games,
                    wins,
                    games > 0 ? (double)wins / games : 0.0,
                    g.Select(x => x.Puuid).Distinct().Count());
            })
            .OrderByDescending(c => c.Games)
            .ThenByDescending(c => c.WinRate)
            .ToList();

        return new ProChampionPlayrateResponse(patch, normalizedRegion, normalizedScope, champions);
    }

    public async Task<List<ProPlayerDto>> ComputeProRosterAsync(
        string? region,
        CancellationToken ct)
    {
        var normalizedRegion = string.IsNullOrWhiteSpace(region) ? "ALL" : region.Trim().ToUpperInvariant();

        var query = _context.TrackedProSummoners
            .AsNoTracking()
            .Where(x => x.IsActive && x.IsPro);

        if (!string.Equals(normalizedRegion, "ALL", StringComparison.Ordinal))
        {
            var platforms = ResolvePlatformsForRegion(normalizedRegion);
            query = query.Where(x => platforms.Contains(x.PlatformRegion.ToUpper()));
        }

        return await query
            .OrderBy(x => x.ProName ?? x.GameName)
            .Select(x => new ProPlayerDto(
                x.ProName,
                x.TeamName,
                x.PlatformRegion,
                x.GameName,
                x.TagLine))
            .ToListAsync(ct);
    }

    internal static string NormalizeProScope(string? scope) =>
        (scope ?? "all").Trim().ToLowerInvariant() switch
        {
            "pro" => "pro",
            "highelo" => "highelo",
            _ => "all"
        };

    private readonly record struct RankTierScope(
        string CacheToken,
        string? ExactTier,
        bool IsEmeraldPlus)
    {
        public bool HasFilter => IsEmeraldPlus || !string.IsNullOrWhiteSpace(ExactTier);
    }

    private static List<int> NormalizeCompletedBuildItems(
        IReadOnlyList<int> itemIds,
        IReadOnlyDictionary<int, BuildItemMetadata> itemMetadataById,
        bool useLegacyFallback)
    {
        var filtered = new List<int>(itemIds.Count);
        foreach (var itemId in itemIds)
        {
            if (itemId == 0)
                continue;

            if (itemMetadataById.TryGetValue(itemId, out var metadata))
            {
                if (!BuildItemClassifier.IsCompletedBuildItem(metadata))
                    continue;

                filtered.Add(itemId);
                continue;
            }

            if (!useLegacyFallback)
                continue;

            if (BuildItemClassifier.LegacyExcludedBuildItems.Contains(itemId))
                continue;

            filtered.Add(itemId);
        }

        filtered.Sort();
        return filtered;
    }

    /// <summary>
    /// Builds the grouping key for a completed build. Collapses builds that share the same core
    /// legendaries (the first <see cref="BuildGroupingCoreSlots"/> items that are global-core) so a
    /// differing situational slot does not fragment the group. Builds with no detectable core items
    /// fall back to their full item set so they still group by exact match rather than collapsing
    /// into a single empty-key bucket.
    /// </summary>
    private static string BuildGroupingKey(IReadOnlyList<int> sortedBuildItems, IReadOnlyCollection<int> globalCoreItems)
    {
        var coreItems = sortedBuildItems
            .Where(globalCoreItems.Contains)
            .Take(BuildGroupingCoreSlots)
            .ToList();

        return coreItems.Count > 0
            ? string.Join(",", coreItems)
            : string.Join(",", sortedBuildItems);
    }

    private readonly record struct BuildPathParticipantInput(
        Guid MatchGuid,
        int ParticipantId,
        bool Win,
        int Spell1Id,
        int Spell2Id);

    private readonly record struct BuildPurchaseRow(
        int PurchaseIndex,
        int ItemId,
        int TimestampMs,
        BuildItemCategory Category);

    private sealed record BuildPathSample(
        bool Win,
        int Spell1Id,
        int Spell2Id,
        IReadOnlyList<BuildPurchaseRow> Purchases,
        string? SkillFirstThree,
        string? SkillMaxOrder);

    private sealed record BuildPathSections(
        List<SummonerSpellPairDto> SummonerSpells,
        SkillOrderDto? SkillOrder,
        List<StarterItemSetDto> StartingItems,
        List<ItemChoiceDto> Boots,
        List<CoreItemStepDto> CoreBuildPath,
        List<SituationalSlotDto> SituationalSlots)
    {
        public static BuildPathSections Empty { get; } = new([], null, [], [], [], []);
    }

    /// <summary>
    /// Loads the ordered timeline purchases + skill orders for the scoped participants and folds them
    /// into the sectioned, timing-aware build path. Returns empty sections when no timeline data exists.
    /// The fetches join against <paramref name="scopedParticipants"/> on (MatchId, ParticipantId) so
    /// only the queried champion's rows are read — not all 10 participants' rows per match.
    /// </summary>
    private async Task<BuildPathSections> ComputeBuildPathSectionsAsync(
        IQueryable<MatchParticipant> scopedParticipants,
        IReadOnlyList<BuildPathParticipantInput> participants,
        CancellationToken ct)
    {
        if (participants.Count == 0)
            return BuildPathSections.Empty;

        var purchaseRows = await scopedParticipants
            .Join(
                _context.MatchParticipantItemPurchases.AsNoTracking(),
                mp => new { mp.MatchId, mp.ParticipantId },
                p => new { p.MatchId, p.ParticipantId },
                (mp, p) => new { p.MatchId, p.ParticipantId, p.PurchaseIndex, p.ItemId, p.TimestampMs, p.Category })
            .ToListAsync(ct);

        var skillRows = await scopedParticipants
            .Join(
                _context.MatchParticipantSkillOrders.AsNoTracking(),
                mp => new { mp.MatchId, mp.ParticipantId },
                s => new { s.MatchId, s.ParticipantId },
                (mp, s) => new { s.MatchId, s.ParticipantId, s.FirstThree, s.MaxOrder })
            .ToListAsync(ct);

        if (purchaseRows.Count == 0 && skillRows.Count == 0)
            return BuildPathSections.Empty;

        var purchasesByParticipant = purchaseRows
            .GroupBy(p => (p.MatchId, p.ParticipantId))
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<BuildPurchaseRow>)g
                    .OrderBy(x => x.PurchaseIndex)
                    .Select(x => new BuildPurchaseRow(x.PurchaseIndex, x.ItemId, x.TimestampMs, x.Category))
                    .ToList());

        var skillByParticipant = skillRows
            .GroupBy(s => (s.MatchId, s.ParticipantId))
            .ToDictionary(g => g.Key, g => g.First());

        var samples = participants
            .Select(p =>
            {
                purchasesByParticipant.TryGetValue((p.MatchGuid, p.ParticipantId), out var purchases);
                skillByParticipant.TryGetValue((p.MatchGuid, p.ParticipantId), out var skill);
                return new BuildPathSample(
                    p.Win,
                    p.Spell1Id,
                    p.Spell2Id,
                    purchases ?? [],
                    skill?.FirstThree,
                    skill?.MaxOrder);
            })
            .ToList();

        return BuildBuildPathSections(samples);
    }

    /// <summary>
    /// Pure aggregation of per-participant build-path samples into the sectioned response: top
    /// summoner-spell pairs, dominant skill order, top starter sets, top boots, the per-position
    /// dominant core path with completion timing, and 4th/5th/6th situational options.
    /// </summary>
    private static BuildPathSections BuildBuildPathSections(IReadOnlyList<BuildPathSample> samples)
    {
        if (samples.Count == 0)
            return BuildPathSections.Empty;

        // Spells come from every participant; the floor scales to the full pool. Purchase-derived
        // sections only have data from the timeline-covered subset, so their floor scales to that
        // subset — otherwise sections are suppressed while the timeline backfill is still rolling out.
        var minGames = Math.Max(2, (int)Math.Ceiling(samples.Count * 0.03));
        var coveredCount = samples.Count(s => s.Purchases.Count > 0);
        var purchaseMinGames = Math.Max(2, (int)Math.Ceiling(coveredCount * 0.03));

        var summonerSpells = samples
            .Where(s => s.Spell1Id > 0 && s.Spell2Id > 0)
            .Select(s => new { s.Win, Lo = Math.Min(s.Spell1Id, s.Spell2Id), Hi = Math.Max(s.Spell1Id, s.Spell2Id) })
            .GroupBy(s => new { s.Lo, s.Hi })
            .Select(g => new SummonerSpellPairDto(g.Key.Lo, g.Key.Hi, g.Count(), (double)g.Count(x => x.Win) / g.Count()))
            .Where(p => p.Games >= minGames)
            .OrderByDescending(p => p.Games)
            .ThenByDescending(p => p.WinRate)
            .Take(3)
            .ToList();

        SkillOrderDto? skillOrder = null;
        var skillSamples = samples.Where(s => !string.IsNullOrEmpty(s.SkillMaxOrder)).ToList();
        if (skillSamples.Count > 0)
        {
            var dominantMax = skillSamples
                .GroupBy(s => s.SkillMaxOrder!)
                .OrderByDescending(g => g.Count())
                .First();
            var dominantFirstThree = samples
                .Where(s => !string.IsNullOrEmpty(s.SkillFirstThree))
                .GroupBy(s => s.SkillFirstThree!)
                .OrderByDescending(g => g.Count())
                .Select(g => g.Key)
                .FirstOrDefault() ?? string.Empty;
            skillOrder = new SkillOrderDto(
                dominantFirstThree,
                dominantMax.Key,
                dominantMax.Count(),
                (double)dominantMax.Count(x => x.Win) / dominantMax.Count());
        }

        var startingItems = samples
            .Select(s => new
            {
                s.Win,
                // Distinct item ids so opening potion-count variation ("Doran's + 1 HP" vs "+ 2 HP")
                // does not fragment the starter set aggregation.
                Set = s.Purchases
                    .Where(p => p.Category == BuildItemCategory.Starter)
                    .Select(p => p.ItemId)
                    .Distinct()
                    .OrderBy(i => i)
                    .ToList()
            })
            .Where(x => x.Set.Count > 0)
            .GroupBy(x => string.Join(",", x.Set))
            .Select(g => new StarterItemSetDto(g.First().Set, g.Count(), (double)g.Count(x => x.Win) / g.Count()))
            .Where(x => x.Games >= purchaseMinGames)
            .OrderByDescending(x => x.Games)
            .ThenByDescending(x => x.WinRate)
            .Take(3)
            .ToList();

        var boots = samples
            .Select(s => new
            {
                s.Win,
                BootId = s.Purchases
                    .Where(p => p.Category == BuildItemCategory.Boots)
                    .OrderBy(p => p.PurchaseIndex)
                    .Select(p => (int?)p.ItemId)
                    .FirstOrDefault()
            })
            .Where(x => x.BootId.HasValue)
            .GroupBy(x => x.BootId!.Value)
            .Select(g => new ItemChoiceDto(g.Key, g.Count(), (double)g.Count(x => x.Win) / g.Count()))
            .Where(x => x.Games >= purchaseMinGames)
            .OrderByDescending(x => x.Games)
            .ThenByDescending(x => x.WinRate)
            .Take(4)
            .ToList();

        // Tally each ordered legendary purchase by its position (0-based) across all participants.
        var positionTally = new Dictionary<int, Dictionary<int, (int Games, int Wins, double SumMinutes)>>();
        foreach (var sample in samples)
        {
            var legendaries = sample.Purchases
                .Where(p => p.Category == BuildItemCategory.Legendary)
                .OrderBy(p => p.PurchaseIndex)
                .ToList();

            for (var position = 0; position < Math.Min(legendaries.Count, MaxBuildPathSlots); position++)
            {
                var legendary = legendaries[position];
                if (!positionTally.TryGetValue(position, out var itemMap))
                {
                    itemMap = new Dictionary<int, (int, int, double)>();
                    positionTally[position] = itemMap;
                }

                var prior = itemMap.GetValueOrDefault(legendary.ItemId);
                itemMap[legendary.ItemId] = (
                    prior.Games + 1,
                    prior.Wins + (sample.Win ? 1 : 0),
                    prior.SumMinutes + legendary.TimestampMs / 60_000.0);
            }
        }

        var coreBuildPath = new List<CoreItemStepDto>();
        var usedCoreItems = new HashSet<int>();
        for (var position = 0; position < CoreBuildPathSlots; position++)
        {
            if (!positionTally.TryGetValue(position, out var itemMap))
                break;

            var pick = itemMap
                .Where(kvp => !usedCoreItems.Contains(kvp.Key) && kvp.Value.Games >= purchaseMinGames)
                .OrderByDescending(kvp => kvp.Value.Games)
                .ThenByDescending(kvp => (double)kvp.Value.Wins / kvp.Value.Games)
                .Select(kvp => (ItemId: kvp.Key, kvp.Value.Games, kvp.Value.Wins, kvp.Value.SumMinutes))
                .FirstOrDefault();

            if (pick.Games == 0)
                break;

            usedCoreItems.Add(pick.ItemId);
            coreBuildPath.Add(new CoreItemStepDto(
                pick.ItemId,
                pick.Games,
                (double)pick.Wins / pick.Games,
                pick.SumMinutes / pick.Games));
        }

        var situationalSlots = new List<SituationalSlotDto>();
        for (var position = CoreBuildPathSlots; position < MaxBuildPathSlots; position++)
        {
            if (!positionTally.TryGetValue(position, out var itemMap))
                continue;

            var options = itemMap
                .Where(kvp => kvp.Value.Games >= purchaseMinGames)
                .OrderByDescending(kvp => kvp.Value.Games)
                .ThenByDescending(kvp => (double)kvp.Value.Wins / kvp.Value.Games)
                .Take(4)
                .Select(kvp => new ItemChoiceDto(kvp.Key, kvp.Value.Games, (double)kvp.Value.Wins / kvp.Value.Games))
                .ToList();

            if (options.Count > 0)
                situationalSlots.Add(new SituationalSlotDto(position + 1, options));
        }

        return new BuildPathSections(summonerSpells, skillOrder, startingItems, boots, coreBuildPath, situationalSlots);
    }

    /// <summary>
    /// Helper record for rune metadata lookup result.
    /// </summary>
    private readonly record struct RuneMetadata(int RunePathId, int Slot);

    private readonly record struct StoredRuneSelection(
        int RuneId,
        RuneSelectionTree SelectionTree,
        int SelectionIndex,
        int StyleId);

    /// <summary>
    /// Helper record for rune information grouping.
    /// </summary>
    private record RuneInfoResult(
        string Key,
        int PrimaryStyleId,
        int SubStyleId,
        List<int> PrimaryRunes,
        List<int> SubRunes,
        List<int> StatShards
    );

    /// <summary>
    /// Builds rune information structure from explicit rune selections (with metadata fallback for legacy rows).
    /// </summary>
    private static RuneInfoResult BuildRuneInfo(
        List<StoredRuneSelection> selections,
        Dictionary<int, RuneMetadata> runeMetadata)
    {
        if (selections.Count == 0)
        {
            return new RuneInfoResult("0:|0:|", 0, 0, [], [], []);
        }

        if (HasStructuredSelections(selections))
        {
            var primaryRunes = selections
                .Where(s => s.SelectionTree == RuneSelectionTree.Primary)
                .OrderBy(s => s.SelectionIndex)
                .Select(s => s.RuneId)
                .ToList();
            var subRunes = selections
                .Where(s => s.SelectionTree == RuneSelectionTree.Secondary)
                .OrderBy(s => s.SelectionIndex)
                .Select(s => s.RuneId)
                .ToList();
            var statShards = selections
                .Where(s => s.SelectionTree == RuneSelectionTree.StatShards)
                .OrderBy(s => s.SelectionIndex)
                .Select(s => s.RuneId)
                .ToList();

            var primaryStyleId = selections
                .Where(s => s.SelectionTree == RuneSelectionTree.Primary && s.StyleId > 0)
                .Select(s => s.StyleId)
                .FirstOrDefault();
            var subStyleId = selections
                .Where(s => s.SelectionTree == RuneSelectionTree.Secondary && s.StyleId > 0)
                .Select(s => s.StyleId)
                .FirstOrDefault();

            if (primaryStyleId == 0 && primaryRunes.Count > 0 &&
                runeMetadata.TryGetValue(primaryRunes[0], out var primaryMeta) &&
                primaryMeta.RunePathId is > 0 and < 5000)
            {
                primaryStyleId = primaryMeta.RunePathId;
            }

            if (subStyleId == 0 && subRunes.Count > 0 &&
                runeMetadata.TryGetValue(subRunes[0], out var subMeta) &&
                subMeta.RunePathId is > 0 and < 5000)
            {
                subStyleId = subMeta.RunePathId;
            }

            var key =
                $"{primaryStyleId}:{string.Join(",", primaryRunes)}|{subStyleId}:{string.Join(",", subRunes)}|{string.Join(",", statShards)}";
            return new RuneInfoResult(key, primaryStyleId, subStyleId, primaryRunes, subRunes, statShards);
        }

        // Legacy fallback for rows missing explicit tree/index/style.
        var runesByPath = new Dictionary<int, List<(int RuneId, int Slot)>>();
        foreach (var selection in selections)
        {
            if (!runeMetadata.TryGetValue(selection.RuneId, out var meta))
                continue;

            if (!runesByPath.ContainsKey(meta.RunePathId))
                runesByPath[meta.RunePathId] = [];
            runesByPath[meta.RunePathId].Add((selection.RuneId, meta.Slot));
        }

        var statShardsFallback = runesByPath
            .Where(kvp => kvp.Key >= 5000)
            .SelectMany(kvp => kvp.Value)
            .OrderBy(x => x.Slot)
            .Select(x => x.RuneId)
            .ToList();

        var nonStatPaths = runesByPath
            .Where(kvp => kvp.Key > 0 && kvp.Key < 5000)
            .Select(kvp => new { PathId = kvp.Key, Runes = kvp.Value.OrderBy(x => x.Slot).ToList() })
            .OrderByDescending(x => x.Runes.Count)
            .ThenBy(x => x.PathId)
            .ToList();

        var primaryPath = nonStatPaths.FirstOrDefault();
        var secondaryPath = nonStatPaths.Skip(1).FirstOrDefault();

        var primaryRunesFallback = primaryPath?.Runes.Select(r => r.RuneId).ToList() ?? [];
        var subRunesFallback = secondaryPath?.Runes.Select(r => r.RuneId).ToList() ?? [];
        var keyFallback =
            $"{primaryPath?.PathId ?? 0}:{string.Join(",", primaryRunesFallback)}|{secondaryPath?.PathId ?? 0}:{string.Join(",", subRunesFallback)}|{string.Join(",", statShardsFallback)}";

        return new RuneInfoResult(
            keyFallback,
            primaryPath?.PathId ?? 0,
            secondaryPath?.PathId ?? 0,
            primaryRunesFallback,
            subRunesFallback,
            statShardsFallback);
    }

    private static bool HasStructuredSelections(List<StoredRuneSelection> selections)
    {
        if (selections.Count == 0)
            return false;

        var hasNonDefaultHierarchy = selections.Any(s =>
            s.SelectionTree != RuneSelectionTree.Primary ||
            s.StyleId != 0);

        if (!hasNonDefaultHierarchy)
            return false;

        var uniqueSlots = selections
            .Select(s => (s.SelectionTree, s.SelectionIndex))
            .Distinct()
            .Count();

        return uniqueSlots == selections.Count;
    }

    /// <summary>
    /// Computes matchup data showing counters (bad matchups) and favorable matchups.
    /// Uses lane-specific self-join: same role, different team.
    /// </summary>
    public async Task<ChampionMatchupsResponse> ComputeMatchupsAsync(
        int championId,
        string role,
        string? rankTier,
        string? region,
        string patch,
        CancellationToken ct)
    {
        var rankTierScope = ParseRankTierScope(rankTier);
        const int minuteMark = 15;
        var normalizedRegion = AnalyticsRegionCatalog.NormalizeOrDefault(region);
        var regionFilter = AnalyticsRegionCatalog.NormalizeToFilter(region);

        var championQuery = _context.MatchParticipants
            .AsNoTracking()
            .Where(mp => mp.ChampionId == championId
                      && mp.TeamPosition == role
                      && mp.Match.Patch == patch
                      && mp.Match.Status == FetchStatus.Success
                      && (mp.Match.QueueId == QueueCatalog.RankedSoloDuoQueueId ||
                          (mp.Match.QueueId == 0 &&
                           mp.Match.QueueType == QueueCatalog.RankedSoloDuoQueueId.ToString())));

        if (!string.IsNullOrWhiteSpace(regionFilter))
        {
            championQuery = championQuery.Where(mp => mp.Summoner.PlatformRegion == regionFilter);
        }

        // Apply rank tier filter if specified
        championQuery = ApplyRankTierScopeToParticipants(
            championQuery,
            rankTierScope,
            _context.Ranks.AsNoTracking());

        var lanePairsQuery = championQuery
            .Join(
                _context.MatchParticipants.AsNoTracking(),
                champion => champion.MatchId,
                opponent => opponent.MatchId,
                (champion, opponent) => new { Champion = champion, Opponent = opponent })
            .Where(x => x.Champion.TeamPosition == x.Opponent.TeamPosition && x.Champion.TeamId != x.Opponent.TeamId)
            .Select(x => new
            {
                x.Champion.MatchId,
                x.Champion.Win,
                OpponentChampionId = x.Opponent.ChampionId,
                ChampionParticipantId = x.Champion.ParticipantId,
                OpponentParticipantId = x.Opponent.ParticipantId
            });

        var timelineSnapshotQuery = _context.MatchParticipantTimelineSnapshots
            .AsNoTracking()
            .Where(s => s.MinuteMark == minuteMark);

        var matchupData = await (
                from pair in lanePairsQuery
                join championTimeline in timelineSnapshotQuery
                    on new { pair.MatchId, ParticipantId = pair.ChampionParticipantId }
                    equals new { championTimeline.MatchId, championTimeline.ParticipantId }
                    into championTimelineRows
                from championTimeline in championTimelineRows.DefaultIfEmpty()
                join opponentTimeline in timelineSnapshotQuery
                    on new { pair.MatchId, ParticipantId = pair.OpponentParticipantId }
                    equals new { opponentTimeline.MatchId, opponentTimeline.ParticipantId }
                    into opponentTimelineRows
                from opponentTimeline in opponentTimelineRows.DefaultIfEmpty()
                group new { pair, championTimeline, opponentTimeline } by pair.OpponentChampionId
                into g
                select new
                {
                    OpponentChampionId = g.Key,
                    Games = g.Count(),
                    Wins = g.Sum(x => x.pair.Win ? 1 : 0),
                    Losses = g.Sum(x => x.pair.Win ? 0 : 1),
                    TimelineGames = g.Count(x => x.championTimeline != null && x.opponentTimeline != null),
                    AvgGoldDiffAt15 = g
                        .Where(x => x.championTimeline != null && x.opponentTimeline != null)
                        .Select(x => (double?)(x.championTimeline!.Gold - x.opponentTimeline!.Gold))
                        .Average(),
                    AvgXpDiffAt15 = g
                        .Where(x => x.championTimeline != null && x.opponentTimeline != null)
                        .Select(x => (double?)(x.championTimeline!.Xp - x.opponentTimeline!.Xp))
                        .Average(),
                    LatestTimelineAtUtc = g
                        .Where(x => x.championTimeline != null)
                        .Select(x => (DateTime?)x.championTimeline!.DerivedAtUtc)
                        .Max()
                })
            .ToListAsync(ct);

        var totalMatchupGames = matchupData.Sum(m => m.Games);
        var totalTimelineGames = matchupData.Sum(m => m.TimelineGames);
        var timelineCoverage = totalMatchupGames > 0
            ? (double)totalTimelineGames / totalMatchupGames
            : (double?)null;
        var timelineFreshness = matchupData
            .Where(x => x.LatestTimelineAtUtc.HasValue)
            .Select(x => x.LatestTimelineAtUtc)
            .Max();

        var effectiveMatchupSampleSize = ResolveEffectiveSampleSize(MinMatchupSampleSize, totalMatchupGames, floor: 2);

        var matchups = matchupData
            .Where(m => m.Games >= effectiveMatchupSampleSize)
            .Select(g => new
            {
                g.OpponentChampionId,
                g.Games,
                g.Wins,
                g.Losses,
                WinRate = g.Games > 0 ? (double)g.Wins / g.Games : 0.0,
                g.AvgGoldDiffAt15,
                g.AvgXpDiffAt15
            })
            .Select(m => new MatchupEntryDto
            {
                OpponentChampionId = m.OpponentChampionId,
                Games = m.Games,
                Wins = m.Wins,
                Losses = m.Losses,
                WinRate = m.Games > 0 ? (double)m.Wins / m.Games : 0.0,
                AvgGoldDiffAt15 = m.AvgGoldDiffAt15,
                AvgXpDiffAt15 = m.AvgXpDiffAt15
            })
            .ToList();

        if (matchups.Count == 0)
        {
            matchups = matchupData
                .Where(m => m.Games >= 1)
                .Select(m => new MatchupEntryDto
                {
                    OpponentChampionId = m.OpponentChampionId,
                    Games = m.Games,
                    Wins = m.Wins,
                    Losses = m.Losses,
                    WinRate = m.Games > 0 ? (double)m.Wins / m.Games : 0.0,
                    AvgGoldDiffAt15 = m.AvgGoldDiffAt15,
                    AvgXpDiffAt15 = m.AvgXpDiffAt15
                })
                .ToList();
        }

        var allMatchups = matchups
            .OrderByDescending(m => m.Games)
            .ThenByDescending(m => m.WinRate)
            .ThenBy(m => m.OpponentChampionId)
            .ToList();

        // Separate counters (low win rate) and favorable (high win rate)
        var counters = matchups
            .Where(m => m.WinRate < 0.48)
            .OrderBy(m => m.WinRate)
            .Take(MatchupsToShow)
            .ToList();

        var favorable = matchups
            .Where(m => m.WinRate > 0.52)
            .OrderByDescending(m => m.WinRate)
            .Take(MatchupsToShow)
            .ToList();

        return new ChampionMatchupsResponse
        {
            ChampionId = championId,
            Role = role,
            RankTier = rankTierScope.CacheToken,
            Region = normalizedRegion,
            Patch = patch,
            Counters = counters,
            FavorableMatchups = favorable,
            AllMatchups = allMatchups,
            TimelineCoverageRatio = timelineCoverage,
            TimelineSampleSize = totalTimelineGames,
            TimelineDataFreshnessUtc = timelineFreshness
        };
    }
}
