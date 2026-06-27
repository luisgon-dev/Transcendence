using Microsoft.EntityFrameworkCore;
using Transcendence.Data;
using Transcendence.Data.Models.LoL.Match;
using Transcendence.Service.Core.Services.Analytics.Models;

namespace Transcendence.Service.Core.Services.Analytics.Implementations;

/// <summary>
/// Composes the sectioned, timing-aware build path (and the shared item/rune normalisation helpers)
/// for champion builds. Extracted verbatim from <see cref="ChampionBuildComputeService"/> so the
/// build-path composition logic lives on its own; behaviour is unchanged.
/// </summary>
internal sealed class ChampionBuildPathBuilder
{
    private const int CoreBuildPathSlots = 3;
    private const int MaxBuildPathSlots = 6;
    // Top builds are grouped on the first N core legendaries rather than the full 6-item set so a
    // single situational-slot difference does not fragment otherwise-identical builds below the
    // sample floor. The completed-item set is sorted ascending, so we cannot recover purchase order
    // here — we use the global-core membership (items appearing in >= CoreItemThreshold of games) as
    // the core signal, which is the same core notion surfaced by globalCoreItems.
    private const int BuildGroupingCoreSlots = 4;

    internal static readonly IReadOnlyDictionary<int, BuildItemMetadata> EmptyItemMetadata =
        new Dictionary<int, BuildItemMetadata>();

    private readonly TranscendenceContext _context;

    public ChampionBuildPathBuilder(TranscendenceContext context)
    {
        _context = context;
    }

    internal static List<int> NormalizeCompletedBuildItems(
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
    internal static string BuildGroupingKey(IReadOnlyList<int> sortedBuildItems, IReadOnlyCollection<int> globalCoreItems)
    {
        var coreItems = sortedBuildItems
            .Where(globalCoreItems.Contains)
            .Take(BuildGroupingCoreSlots)
            .ToList();

        return coreItems.Count > 0
            ? string.Join(",", coreItems)
            : string.Join(",", sortedBuildItems);
    }

    internal readonly record struct BuildPathParticipantInput(
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

    internal sealed record BuildPathSections(
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
    internal async Task<BuildPathSections> ComputeBuildPathSectionsAsync(
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
    internal readonly record struct RuneMetadata(int RunePathId, int Slot);

    internal readonly record struct StoredRuneSelection(
        int RuneId,
        RuneSelectionTree SelectionTree,
        int SelectionIndex,
        int StyleId);

    /// <summary>
    /// Helper record for rune information grouping.
    /// </summary>
    internal record RuneInfoResult(
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
    internal static RuneInfoResult BuildRuneInfo(
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
}
