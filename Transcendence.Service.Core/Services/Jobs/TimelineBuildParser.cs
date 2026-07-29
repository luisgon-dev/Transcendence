using Transcendence.Data.Models.LoL.Match;
using Transcendence.Service.Core.Services.Analytics;

namespace Transcendence.Service.Core.Services.Jobs;

/// <summary>A minimal, provider-agnostic projection of a Riot timeline event used by the parser.</summary>
public readonly record struct TimelineBuildEvent(
    string? Type,
    int? ParticipantId,
    int? ItemId,
    int? SkillSlot,
    string? LevelUpType,
    long Timestamp,
    int? BeforeId,
    int? AfterId);

/// <summary>One build-relevant acquisition, net of undo/sell/destroy.</summary>
public readonly record struct BuildPurchase(int ItemId, int TimestampMs, BuildItemCategory Category);

/// <summary>A participant's ordered build-relevant purchase path.</summary>
public sealed record ParticipantBuildPath(int ParticipantId, IReadOnlyList<BuildPurchase> Purchases);

/// <summary>One lossless item lifecycle event, including components and reversals.</summary>
public readonly record struct ParticipantItemLifecycleEvent(
    int ParticipantId,
    int EventIndex,
    MatchItemEventType EventType,
    int TimestampMs,
    int? ItemId,
    int? BeforeId,
    int? AfterId,
    bool IsBuildRelevant,
    BuildItemCategory? BuildCategory);

/// <summary>A participant's derived skill-leveling summary.</summary>
public sealed record ParticipantSkillSequence(int ParticipantId, string Sequence, string FirstThree, string MaxOrder);

/// <summary>
/// Pure (no I/O) parsing of Riot timeline events into ordered, build-relevant item purchases and
/// per-participant skill orders. Kept separate from the Hangfire job so it is unit-testable without
/// fabricating Camille objects: the job projects Camille <c>EventsTimeLine</c> rows into
/// <see cref="TimelineBuildEvent"/> and delegates here.
/// </summary>
public static class TimelineBuildParser
{
    public const string ItemPurchasedType = "ITEM_PURCHASED";
    public const string ItemSoldType = "ITEM_SOLD";
    public const string ItemUndoType = "ITEM_UNDO";
    public const string ItemDestroyedType = "ITEM_DESTROYED";
    public const string SkillLevelUpType = "SKILL_LEVEL_UP";
    public const string ChampionKillType = "CHAMPION_KILL";
    public const string BuildingKillType = "BUILDING_KILL";
    public const string EliteMonsterKillType = "ELITE_MONSTER_KILL";

    // The raw jsonb payload table is written for exactly two consumers: the modeler's pre-decision
    // state query (the three kill/objective types it filters on) and lossless item-lifecycle replay.
    // Every other Match-V5 event type is ~1 KB of jsonb nobody reads, so it is never persisted.
    private static readonly HashSet<string> PersistedPayloadEventTypes =
    [
        ItemPurchasedType,
        ItemSoldType,
        ItemUndoType,
        ItemDestroyedType,
        ChampionKillType,
        BuildingKillType,
        EliteMonsterKillType
    ];

    public static bool IsPersistedPayloadEvent(string? type) =>
        type is not null && PersistedPayloadEventTypes.Contains(type);

    /// <summary>
    /// Projects every item lifecycle event without netting it out. Event indexes are stable per
    /// participant after chronological ordering and allow exact inventory replay.
    /// </summary>
    public static IReadOnlyList<ParticipantItemLifecycleEvent> BuildItemLifecycle(
        IEnumerable<TimelineBuildEvent> events,
        Func<int, BuildItemMetadata?> itemLookup,
        int starterCutoffMs = BuildItemClassifier.DefaultStarterCutoffMs)
    {
        var result = new List<ParticipantItemLifecycleEvent>();
        var indexes = new Dictionary<int, int>();

        foreach (var timelineEvent in events
                     .Where(e => e.ParticipantId is > 0 && IsItemLifecycleEvent(e.Type))
                     .OrderBy(e => e.Timestamp))
        {
            var participantId = timelineEvent.ParticipantId!.Value;
            var eventIndex = indexes.GetValueOrDefault(participantId);
            indexes[participantId] = eventIndex + 1;

            var eventType = timelineEvent.Type switch
            {
                ItemPurchasedType => MatchItemEventType.Purchased,
                ItemSoldType => MatchItemEventType.Sold,
                ItemUndoType => MatchItemEventType.Undo,
                ItemDestroyedType => MatchItemEventType.Destroyed,
                _ => throw new InvalidOperationException("Unsupported item lifecycle event.")
            };

            var classificationItemId = timelineEvent.ItemId is > 0
                ? timelineEvent.ItemId
                : timelineEvent.AfterId is > 0
                    ? timelineEvent.AfterId
                    : timelineEvent.BeforeId;
            BuildItemCategory? category = null;
            if (classificationItemId is > 0)
            {
                var metadata = itemLookup(classificationItemId.Value);
                if (metadata.HasValue)
                {
                    category = BuildItemClassifier.Classify(
                        metadata.Value,
                        (int)timelineEvent.Timestamp,
                        starterCutoffMs);
                }
            }

            result.Add(new ParticipantItemLifecycleEvent(
                participantId,
                eventIndex,
                eventType,
                (int)timelineEvent.Timestamp,
                timelineEvent.ItemId,
                timelineEvent.BeforeId,
                timelineEvent.AfterId,
                category.HasValue,
                category));
        }

        return result;
    }

    /// <summary>
    /// Replays purchase/undo/sell/destroy events per participant into an ordered acquisition list,
    /// then filters to build-relevant items (completed legendaries, boots upgrades, and opening
    /// starters) and tags each with its category. Trinkets/wards and mid-game (post-opening)
    /// components/consumables are dropped; opening-phase purchases — including starter consumables
    /// like potions — are kept as the starter set. Events should be supplied in natural timeline
    /// order (frame, then event); a stable sort by timestamp guards against any out-of-order frames.
    /// </summary>
    public static IReadOnlyList<ParticipantBuildPath> BuildPurchasePaths(
        IEnumerable<TimelineBuildEvent> events,
        Func<int, BuildItemMetadata?> itemLookup,
        int starterCutoffMs = BuildItemClassifier.DefaultStarterCutoffMs)
    {
        var ordered = events
            .Where(e => e.ParticipantId is > 0)
            .OrderBy(e => e.Timestamp)
            .ToList();

        var acquisitionsByParticipant = new Dictionary<int, List<(int ItemId, int TimestampMs)>>();

        foreach (var e in ordered)
        {
            var participantId = e.ParticipantId!.Value;
            switch (e.Type)
            {
                case ItemPurchasedType when e.ItemId is > 0:
                    AcquisitionsFor(acquisitionsByParticipant, participantId)
                        .Add((e.ItemId.Value, (int)e.Timestamp));
                    break;

                case ItemUndoType:
                    // Undo of a purchase removes the bought item (BeforeId); undo of a sell restores it (AfterId).
                    if (e.BeforeId is > 0)
                        RemoveLast(AcquisitionsFor(acquisitionsByParticipant, participantId), e.BeforeId.Value);
                    if (e.AfterId is > 0)
                        AcquisitionsFor(acquisitionsByParticipant, participantId)
                            .Add((e.AfterId.Value, (int)e.Timestamp));
                    break;

                case ItemSoldType when e.ItemId is > 0:
                case ItemDestroyedType when e.ItemId is > 0:
                    RemoveLast(AcquisitionsFor(acquisitionsByParticipant, participantId), e.ItemId.Value);
                    break;
            }
        }

        var result = new List<ParticipantBuildPath>();
        foreach (var (participantId, acquisitions) in acquisitionsByParticipant.OrderBy(kvp => kvp.Key))
        {
            var purchases = new List<BuildPurchase>(acquisitions.Count);
            foreach (var (itemId, timestampMs) in acquisitions)
            {
                var metadata = itemLookup(itemId);
                if (metadata is null)
                    continue;

                var category = BuildItemClassifier.Classify(metadata.Value, timestampMs, starterCutoffMs);
                if (category is null)
                    continue;

                purchases.Add(new BuildPurchase(itemId, timestampMs, category.Value));
            }

            if (purchases.Count > 0)
                result.Add(new ParticipantBuildPath(participantId, purchases));
        }

        return result;
    }

    /// <summary>
    /// Derives each participant's skill-leveling sequence, opening first-three, and basic-ability
    /// max priority from <c>SKILL_LEVEL_UP</c> events (ability evolutions are excluded).
    /// </summary>
    public static IReadOnlyList<ParticipantSkillSequence> BuildSkillSequences(IEnumerable<TimelineBuildEvent> events)
    {
        var levelUps = events
            .Where(e => string.Equals(e.Type, SkillLevelUpType, StringComparison.Ordinal)
                        && e.ParticipantId is > 0
                        && e.SkillSlot is >= 1 and <= 4
                        && (string.IsNullOrEmpty(e.LevelUpType) ||
                            string.Equals(e.LevelUpType, "NORMAL", StringComparison.OrdinalIgnoreCase)))
            // Riot's timeline has emitted duplicate SKILL_LEVEL_UP events since patch 15.17
            // (dev-relations issue #1100): the same (participant, slot, timestamp) point appears
            // multiple times, which would otherwise inflate the sequence, first-three and max-order.
            // A legitimate level-up never shares an exact (participant, slot, timestamp), so collapsing
            // exact duplicates removes only the bug noise.
            .GroupBy(e => (e.ParticipantId, e.SkillSlot, e.Timestamp))
            .Select(g => g.First())
            .OrderBy(e => e.Timestamp)
            .ToList();

        var result = new List<ParticipantSkillSequence>();
        foreach (var group in levelUps.GroupBy(e => e.ParticipantId!.Value).OrderBy(g => g.Key))
        {
            var slots = group.Select(e => e.SkillSlot!.Value).ToList();
            if (slots.Count == 0)
                continue;

            var sequence = string.Join(",", slots.Select(SlotToLetter));
            var firstThree = string.Concat(slots.Take(3).Select(SlotToLetter));
            var maxOrder = BuildMaxOrder(slots);
            result.Add(new ParticipantSkillSequence(group.Key, sequence, firstThree, maxOrder));
        }

        return result;
    }

    private static List<(int ItemId, int TimestampMs)> AcquisitionsFor(
        Dictionary<int, List<(int ItemId, int TimestampMs)>> map,
        int participantId)
    {
        if (!map.TryGetValue(participantId, out var list))
        {
            list = [];
            map[participantId] = list;
        }

        return list;
    }

    private static bool IsItemLifecycleEvent(string? type) =>
        type is ItemPurchasedType or ItemSoldType or ItemUndoType or ItemDestroyedType;

    private static void RemoveLast(List<(int ItemId, int TimestampMs)> acquisitions, int itemId)
    {
        for (var i = acquisitions.Count - 1; i >= 0; i--)
        {
            if (acquisitions[i].ItemId == itemId)
            {
                acquisitions.RemoveAt(i);
                return;
            }
        }
    }

    private static string BuildMaxOrder(IReadOnlyList<int> slots)
    {
        int FirstIndexOf(int slot)
        {
            for (var i = 0; i < slots.Count; i++)
                if (slots[i] == slot)
                    return i;
            return int.MaxValue;
        }

        // Rank basic abilities (Q/W/E) by points spent, tie-broken by which was leveled first.
        var ranking = new[] { 1, 2, 3 }
            .Select(slot => (slot, count: slots.Count(s => s == slot), first: FirstIndexOf(slot)))
            .Where(x => x.count > 0)
            .OrderByDescending(x => x.count)
            .ThenBy(x => x.first)
            .Select(x => SlotToLetter(x.slot));

        return string.Join(">", ranking);
    }

    private static string SlotToLetter(int slot) => slot switch
    {
        1 => "Q",
        2 => "W",
        3 => "E",
        4 => "R",
        _ => "?"
    };
}
