using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Transcendence.Data;
using Transcendence.Service.Core.Queries;
using Transcendence.Service.Core.Services.Analytics.Interfaces;
using Transcendence.Service.Core.Services.Analytics.Models;

namespace Transcendence.Service.Core.Services.Analytics.Implementations;

/// <summary>
/// Raw + stats-backed computation for champion builds. Extracted from the original analytics compute
/// service (P10.1) so this domain is a focused unit; win rates / tier lists, pro builds/playrate, and
/// matchups (<see cref="ChampionMatchupComputeService"/>) live in their own services. Behavior is identical
/// to the pre-extraction code — the analytics test suite (raw + raw-vs-stats build equivalence) is the gate.
/// </summary>
public sealed class ChampionBuildComputeService : IChampionBuildComputeService
{
    private const double CoreItemThreshold = 0.70;
    private const int MinBuildSampleSize = 30;
    private const double ItemMetadataCoverageFallbackThreshold = 0.90;

    private readonly TranscendenceContext _context;
    private readonly ChampionAnalyticsComputeOptions _options;
    private readonly ILogger<ChampionBuildComputeService> _logger;

    public ChampionBuildComputeService(
        TranscendenceContext context,
        IOptions<ChampionAnalyticsComputeOptions> options,
        ILogger<ChampionBuildComputeService> logger)
    {
        _context = context;
        _options = options.Value;
        _logger = logger;
    }

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
        var minimumGamesRequired = await AnalyticsSampleThreshold.ResolveAsync(_context, _options, patch, ct);
        var rankTierScope = AnalyticsScopeMath.ParseRankTierScope(rankTier);
        var normalizedRegion = AnalyticsRegionCatalog.NormalizeOrDefault(region);
        var regionFilter = AnalyticsRegionCatalog.NormalizeToFilter(region);

        // Step 1: Get all match data for this champion/role/patch/tier with items and runes
        var baseQuery = _context.MatchParticipants
            .AsNoTracking()
            .AsSplitQuery()
            .Include(mp => mp.Items)
            .Include(mp => mp.Runes)
            .Where(mp => mp.ChampionId == championId && mp.TeamPosition == role)
            .OnPatch(patch)
            .FromSuccessfulMatches()
            .InRankedSoloQueue();

        baseQuery = baseQuery.InPlatformRegion(regionFilter);

        baseQuery = AnalyticsScopeMath.ApplyRankTierScopeToParticipants(baseQuery, rankTierScope, _context.Ranks.AsNoTracking());

        var matchData = await baseQuery
            .Select(mp => new
            {
                mp.Win,
                MatchGuid = mp.Match.Id,
                mp.ParticipantId,
                mp.SummonerSpell1Id,
                mp.SummonerSpell2Id,
                Items = mp.Items.Select(i => i.ItemId).ToList(),
                Runes = mp.Runes.Select(r => new ChampionBuildPathBuilder.StoredRuneSelection(
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
                Items = ChampionBuildPathBuilder.NormalizeCompletedBuildItems(m.Items, itemMetadataById, useLegacyFallback)
            })
            .Where(m => m.Items.Count > 0)
            .ToList();

        var effectiveMinimumGames = AnalyticsScopeMath.ResolveEffectiveSampleSize(minimumGamesRequired, buildEligibleMatches.Count, floor: 3);
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
            .ToDictionaryAsync(rv => rv.RuneId, rv => new ChampionBuildPathBuilder.RuneMetadata(rv.RunePathId, rv.Slot), ct);

        // Step 4: Group by build (items + runes as key)
        var effectiveBuildSampleSize = AnalyticsScopeMath.ResolveEffectiveSampleSize(MinBuildSampleSize, totalGames, floor: 2);
        var buildGroups = buildEligibleMatches
            .Select(m => new
            {
                m.Win,
                // Group on the first N core legendaries, not the full 6-item set (see BuildGroupingKey).
                ItemKey = ChampionBuildPathBuilder.BuildGroupingKey(m.Items, globalCoreItems),
                // Build rune structure
                RuneInfo = ChampionBuildPathBuilder.BuildRuneInfo(m.Runes, runeMetadata),
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
                    ItemKey = ChampionBuildPathBuilder.BuildGroupingKey(m.Items, globalCoreItems),
                    RuneInfo = ChampionBuildPathBuilder.BuildRuneInfo(m.Runes, runeMetadata),
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
        var sections = await new ChampionBuildPathBuilder(_context).ComputeBuildPathSectionsAsync(
            baseQuery,
            matchData.Select(m => new ChampionBuildPathBuilder.BuildPathParticipantInput(
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
}
