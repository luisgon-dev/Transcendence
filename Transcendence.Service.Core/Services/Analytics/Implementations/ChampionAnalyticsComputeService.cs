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
        var normalizedRankTierFilter = NormalizeRankTierFilter(filter.RankTier);

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
            baseQuery = baseQuery.Where(mp => mp.Summoner.Region == filter.Region);
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
        if (!string.IsNullOrEmpty(normalizedRankTierFilter))
        {
            participantRanks = participantRanks
                .Where(pr => pr.RankTier == normalizedRankTierFilter);
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
        string patch,
        CancellationToken ct)
    {
        var normalizedRole = string.IsNullOrWhiteSpace(role) ? "ALL" : role.ToUpperInvariant();
        var isUnifiedRole = normalizedRole == "ALL";
        var normalizedRankTierFilter = NormalizeRankTierFilter(rankTier);
        var minimumGamesRequired = await GetAdaptiveMinimumGamesRequiredAsync(patch, ct);

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

        // Only apply rank join semantics when a tier filter is requested.
        // Unfiltered views intentionally keep unranked participants.
        if (!string.IsNullOrEmpty(normalizedRankTierFilter))
        {
            baseQuery = baseQuery.Where(mp => _context.Ranks.Any(r =>
                r.QueueType == "RANKED_SOLO_5x5" &&
                r.SummonerId == mp.SummonerId &&
                r.Tier == normalizedRankTierFilter));
        }

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

        // Step 5: Get previous patch tier list for movement comparison
        var previousPatch = await GetPreviousPatchAsync(patch, ct);
        var previousTiers = previousPatch != null
            ? await GetPreviousPatchTiersAsync(normalizedRole, normalizedRankTierFilter, previousPatch, ct)
            : new Dictionary<(int ChampionId, string Role), TierGrade>();

        // Step 6: Assign percentile-based tiers
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

            // Calculate movement from previous patch
            var lookupKey = (entry.ChampionId, NormalizeRole(entry.TeamPosition));
            var previousTier = previousTiers.GetValueOrDefault(lookupKey);
            var movement = CalculateMovement(tier, previousTier);

            return new TierListEntry(
                entry.ChampionId,
                entry.TeamPosition,
                tier,
                entry.CompositeScore,
                entry.WinRate,
                entry.PickRate,
                entry.BanRate,
                entry.Games,
                movement,
                previousTier
            );
        }).ToList();
    }

    /// <summary>
    /// Gets the patch version immediately before the specified patch.
    /// </summary>
    private async Task<string?> GetPreviousPatchAsync(string currentPatch, CancellationToken ct)
    {
        // Get all patches ordered by release date, find the one before current
        var patches = await _context.Patches
            .AsNoTracking()
            .OrderByDescending(p => p.ReleaseDate)
            .Select(p => p.Version)
            .Take(10)
            .ToListAsync(ct);

        var currentIndex = patches.IndexOf(currentPatch);
        return currentIndex >= 0 && currentIndex + 1 < patches.Count
            ? patches[currentIndex + 1]
            : null;
    }

    /// <summary>
    /// Gets tier assignments from previous patch for movement comparison.
    /// </summary>
    private async Task<Dictionary<(int ChampionId, string Role), TierGrade>> GetPreviousPatchTiersAsync(
        string role,
        string? rankTier,
        string patch,
        CancellationToken ct)
    {
        // Simplified query - just need champion -> tier mapping
        var previousList = await ComputeTierListAsync(role, rankTier, patch, ct);
        return previousList
            .GroupBy(e => (e.ChampionId, Role: NormalizeRole(e.Role)))
            .ToDictionary(g => g.Key, g => g.First().Tier);
    }

    private async Task<int> GetAdaptiveMinimumGamesRequiredAsync(string patch, CancellationToken ct)
    {
        var steadyStateMinimum = Math.Max(1, _options.MinimumGamesRequired);
        var earlyPatchMinimum = Math.Clamp(_options.EarlyPatchMinimumGamesRequired, 1, steadyStateMinimum);
        var earlyPatchWindowHours = Math.Max(0, _options.EarlyPatchWindowHours);

        if (earlyPatchWindowHours == 0)
            return steadyStateMinimum;

        var releaseDate = await _context.Patches
            .AsNoTracking()
            .Where(p => p.Version == patch)
            .Select(p => (DateTime?)p.ReleaseDate)
            .FirstOrDefaultAsync(ct);

        if (!releaseDate.HasValue)
            return steadyStateMinimum;

        var isEarlyPatchWindow = DateTime.UtcNow < releaseDate.Value.AddHours(earlyPatchWindowHours);
        return isEarlyPatchWindow ? earlyPatchMinimum : steadyStateMinimum;
    }

    private static string? NormalizeRankTierFilter(string? rankTier)
    {
        if (string.IsNullOrWhiteSpace(rankTier))
            return null;

        var normalized = rankTier.Trim().ToUpperInvariant();
        return normalized == "ALL" ? null : normalized;
    }

    private static string NormalizeRole(string? role) =>
        string.IsNullOrWhiteSpace(role) ? "ALL" : role.ToUpperInvariant();

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
            roleQuery = roleQuery.Where(mp => mp.Summoner.Region == region);

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

    /// <summary>
    /// Calculates movement indicator by comparing tier grades.
    /// </summary>
    private static TierMovement CalculateMovement(TierGrade currentTier, TierGrade? previousTier)
    {
        if (!previousTier.HasValue)
            return TierMovement.NEW;

        var currentValue = TierToValue(currentTier);
        var previousValue = TierToValue(previousTier.Value);

        return (currentValue - previousValue) switch
        {
            > 0 => TierMovement.UP,
            < 0 => TierMovement.DOWN,
            _ => TierMovement.SAME
        };
    }

    /// <summary>
    /// Converts tier grade to numeric value for comparison.
    /// </summary>
    private static int TierToValue(TierGrade tier) => tier switch
    {
        TierGrade.S => 5,
        TierGrade.A => 4,
        TierGrade.B => 3,
        TierGrade.C => 2,
        TierGrade.D => 1,
        _ => 0
    };

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

    // Non-build-impact item classes that should not appear in completed build recommendations.
    private static readonly HashSet<string> ExcludedBuildItemCategories = new(StringComparer.OrdinalIgnoreCase)
    {
        "Consumable",
        "Trinket",
        "Vision"
    };

    // Used only when item metadata coverage is incomplete for the active patch.
    private static readonly HashSet<int> LegacyExcludedBuildItems = new()
    {
        // Trinkets and wards
        3340, 3363, 3364, 2055,
        // Consumables
        2003, 2010, 2031, 2033, 2138, 2139, 2140,
        // Starter and component items
        1001, 1004, 1011, 1018, 1026, 1027, 1028, 1029, 1031, 1033, 1035, 1036, 1037, 1038, 1042, 1043,
        1052, 1053, 1054, 1055, 1056, 1057, 1058, 1082, 1083, 2420, 2421, 2422, 3024, 3052, 3070
    };

    private const double CoreItemThreshold = 0.70;
    private const int MinBuildSampleSize = 30;
    private const double ItemMetadataCoverageFallbackThreshold = 0.90;

    /// <summary>
    /// Computes top 3 builds for a champion with items and runes bundled.
    /// Core items (70%+ appearance) distinguished from situational.
    /// </summary>
    public async Task<ChampionBuildsResponse> ComputeBuildsAsync(
        int championId,
        string role,
        string? rankTier,
        string patch,
        CancellationToken ct)
    {
        var minimumGamesRequired = await GetAdaptiveMinimumGamesRequiredAsync(patch, ct);
        var normalizedRankTierFilter = NormalizeRankTierFilter(rankTier);

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

        if (!string.IsNullOrEmpty(normalizedRankTierFilter))
        {
            baseQuery = baseQuery.Where(mp => _context.Ranks.Any(r =>
                r.QueueType == "RANKED_SOLO_5x5" &&
                r.SummonerId == mp.SummonerId &&
                r.Tier == normalizedRankTierFilter));
        }

        var matchData = await baseQuery
            .Select(mp => new
            {
                mp.Win,
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
            return new ChampionBuildsResponse(championId, role, rankTier ?? "all", patch,
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
                ItemKey = string.Join(",", m.Items),
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
                    ItemKey = string.Join(",", m.Items),
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

        return new ChampionBuildsResponse(
            championId,
            role,
            rankTier ?? "all",
            patch,
            globalCoreItems,
            builds
        );
    }

    public async Task<ChampionProBuildsResponse> ComputeProBuildsAsync(
        int championId,
        string? region,
        string? role,
        string patch,
        CancellationToken ct)
    {
        var normalizedRegion = string.IsNullOrWhiteSpace(region) ? "ALL" : region.Trim().ToUpperInvariant();
        var normalizedRole = string.IsNullOrWhiteSpace(role) ? "ALL" : role.Trim().ToUpperInvariant();

        var proQuery = _context.TrackedProSummoners
            .AsNoTracking()
            .Where(x => x.IsActive);

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
            return new ChampionProBuildsResponse(championId, patch, normalizedRole, normalizedRegion, [], [], []);

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

        var rows = await participantQuery
            .Select(mp => new
            {
                mp.Match.MatchId,
                mp.Match.MatchDate,
                mp.Win,
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
            return new ChampionProBuildsResponse(championId, patch, normalizedRole, normalizedRegion, [], [], []);

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

        var projectedRows = rows
            .Select(r =>
            {
                var runeInfo = BuildRuneInfo(r.Runes, runeMetadata);
                rosterByPuuid.TryGetValue(r.Puuid ?? string.Empty, out var roster);
                var playerName = !string.IsNullOrWhiteSpace(roster?.ProName)
                    ? roster.ProName
                    : (r.GameName != null && r.TagLine != null ? $"{r.GameName}#{r.TagLine}" : r.GameName);

                return new
                {
                    r.MatchId,
                    r.MatchDate,
                    r.Win,
                    PlayerName = playerName,
                    TeamName = roster?.TeamName,
                    Items = r.Items.Where(i => i != 0).OrderBy(i => i).ToList(),
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
                r.RuneInfo.StatShards))
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

        var commonBuilds = projectedRows
            .GroupBy(r => string.Join(",", r.Items))
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
            recentMatches,
            topPlayers,
            commonBuilds);
    }

    private readonly record struct BuildItemMetadata(
        IReadOnlyList<int> BuildsFrom,
        IReadOnlyList<int> BuildsInto,
        IReadOnlyList<string> Tags,
        bool InStore,
        int PriceTotal);

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
                if (!IsCompletedBuildItem(metadata))
                    continue;

                filtered.Add(itemId);
                continue;
            }

            if (!useLegacyFallback)
                continue;

            if (LegacyExcludedBuildItems.Contains(itemId))
                continue;

            filtered.Add(itemId);
        }

        filtered.Sort();
        return filtered;
    }

    private static bool IsCompletedBuildItem(BuildItemMetadata metadata)
    {
        if (!metadata.InStore)
            return false;

        if (metadata.PriceTotal <= 0)
            return false;

        if (metadata.BuildsInto.Count > 0)
            return false;

        if (metadata.BuildsFrom.Count == 0)
            return false;

        return metadata.Tags.All(tag => !ExcludedBuildItemCategories.Contains(tag));
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
        string patch,
        CancellationToken ct)
    {
        var normalizedRankTierFilter = NormalizeRankTierFilter(rankTier);
        const int minuteMark = 15;

        var championQuery = _context.MatchParticipants
            .AsNoTracking()
            .Where(mp => mp.ChampionId == championId
                      && mp.TeamPosition == role
                      && mp.Match.Patch == patch
                      && mp.Match.Status == FetchStatus.Success
                      && (mp.Match.QueueId == QueueCatalog.RankedSoloDuoQueueId ||
                          (mp.Match.QueueId == 0 &&
                           mp.Match.QueueType == QueueCatalog.RankedSoloDuoQueueId.ToString())));

        // Apply rank tier filter if specified
        if (!string.IsNullOrEmpty(normalizedRankTierFilter))
        {
            championQuery = championQuery.Where(mp => _context.Ranks.Any(r =>
                r.QueueType == "RANKED_SOLO_5x5" &&
                r.SummonerId == mp.SummonerId &&
                r.Tier == normalizedRankTierFilter));
        }

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
            RankTier = rankTier ?? "all",
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
