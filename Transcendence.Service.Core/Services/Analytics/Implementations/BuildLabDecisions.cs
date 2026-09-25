using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Transcendence.Data.Models.LoL.Analytics;
using Transcendence.Data.Models.LoL.Match;

namespace Transcendence.Service.Core.Services.Analytics.Implementations;

/// <summary>One build decision a participant made: what they chose, and what they had chosen before it.</summary>
public sealed record BuildLabDecision(
    BuildLabFamily Family,
    short Stage,
    IReadOnlyList<int> Prefix,
    IReadOnlyList<int> ActionIds,
    int? TimestampMs)
{
    public string ActionKey => BuildLabPath.ActionKey(ActionIds);
}

/// <summary>The item-event fields the replay reads.</summary>
public readonly record struct BuildLabItemEvent(
    int EventIndex,
    MatchItemEventType EventType,
    int TimestampMs,
    int? ItemId,
    int? BeforeId,
    int? AfterId,
    BuildItemCategory? BuildCategory);

/// <summary>The rune fields the replay reads.</summary>
public readonly record struct BuildLabRune(RuneSelectionTree Tree, int Index, int RuneId);

public static class BuildLabPath
{
    /// <summary>
    /// The one definition of a path's identity, shared by the refresher that writes a prefix and the
    /// service that looks it up: the first 8 bytes of SHA-256 over the comma-joined ids, in order.
    /// </summary>
    public static long Hash(IReadOnlyList<int> path)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join(",", path)));
        return BinaryPrimitives.ReadInt64BigEndian(bytes);
    }

    public static string ActionKey(IReadOnlyList<int> ids) => string.Join("+", ids);

    public static IReadOnlyList<int> ParseActionKey(string key) =>
        key.Split('+', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => int.TryParse(part, out var id) ? id : 0)
            .Where(id => id > 0)
            .ToArray();
}

public static class BuildLabDecisions
{
    /// <summary>The number of completed legendaries Build Lab follows.</summary>
    public const int MaximumItemStage = 6;

    /// <summary>
    /// Starter-category purchases after this are refills, not the opening buy: potions and wards are
    /// starter items too, and a potion bought at 15 minutes is not part of anyone's start.
    /// </summary>
    public const int StarterWindowMs = 90_000;

    /// <summary>The gold bucket every pregame decision (and any decision without frames) falls in.</summary>
    public const short NeutralGoldBucket = 2;

    // Team gold difference edges. Wide enough that the outer buckets are genuinely "far ahead/behind"
    // rather than ordinary lane noise, which sits comfortably inside +-750 in the first minutes.
    private static readonly int[] GoldBucketEdges = [-2500, -750, 750, 2500];

    public static short GoldBucket(int? teamGoldDifference)
    {
        if (teamGoldDifference is not { } difference)
            return NeutralGoldBucket;
        short bucket = 0;
        foreach (var edge in GoldBucketEdges)
        {
            if (difference < edge)
                return bucket;
            bucket++;
        }
        return bucket;
    }

    /// <summary>
    /// Replays one participant's item lifecycle into the item decisions they made.
    ///
    /// An undo removes the purchase it reverses, so an item bought and immediately refunded never
    /// counts. A legendary acquired a second time (sold, then bought back) is not a new decision.
    /// </summary>
    public static IEnumerable<BuildLabDecision> Items(IEnumerable<BuildLabItemEvent> events)
    {
        var ordered = events
            .OrderBy(itemEvent => itemEvent.TimestampMs)
            .ThenBy(itemEvent => itemEvent.EventIndex)
            .ToList();
        var undone = UndonePurchases(ordered);

        var starters = new List<int>();
        var legendaries = new List<int>();
        var bootsSeen = false;
        var decisions = new List<BuildLabDecision>();
        foreach (var itemEvent in ordered)
        {
            if (itemEvent.EventType != MatchItemEventType.Purchased ||
                itemEvent.ItemId is not > 0 ||
                undone.Contains(itemEvent.EventIndex))
                continue;

            var itemId = itemEvent.ItemId.Value;
            switch (itemEvent.BuildCategory)
            {
                case BuildItemCategory.Starter when itemEvent.TimestampMs <= StarterWindowMs:
                    starters.Add(itemId);
                    break;
                case BuildItemCategory.Boots when !bootsSeen:
                    bootsSeen = true;
                    decisions.Add(new BuildLabDecision(
                        BuildLabFamily.Boots, 1, [], [itemId], itemEvent.TimestampMs));
                    break;
                case BuildItemCategory.Legendary
                    when legendaries.Count < MaximumItemStage && !legendaries.Contains(itemId):
                    decisions.Add(new BuildLabDecision(
                        BuildLabFamily.Item,
                        (short)(legendaries.Count + 1),
                        legendaries.ToArray(),
                        [itemId],
                        itemEvent.TimestampMs));
                    legendaries.Add(itemId);
                    break;
            }
        }

        if (starters.Count > 0)
        {
            starters.Sort();
            decisions.Insert(0, new BuildLabDecision(BuildLabFamily.Starter, 0, [], starters, 0));
        }
        return decisions;
    }

    /// <summary>
    /// The complete page as one choice, then each rune slot conditioned on the keystone: slot 1 is the
    /// keystone itself, and every later slot is "given this keystone, which rune here?".
    ///
    /// Conditioning on the keystone rather than on every rune before the slot is deliberate. A full
    /// ordered prefix made nearly every page its own key (64k distinct keys from 3,000 matches), so no
    /// slot past the second ever had a sample worth showing; the keystone is the choice that actually
    /// shapes the rest of the page.
    /// </summary>
    public static IEnumerable<BuildLabDecision> Runes(IEnumerable<BuildLabRune> runes)
    {
        var page = runes
            .Where(rune => rune.RuneId > 0)
            .OrderBy(rune => rune.Tree)
            .ThenBy(rune => rune.Index)
            .Select(rune => rune.RuneId)
            .ToArray();
        if (page.Length == 0)
            yield break;

        yield return new BuildLabDecision(BuildLabFamily.RunePage, 0, [], page, null);
        yield return new BuildLabDecision(BuildLabFamily.Rune, 1, [], [page[0]], null);
        int[] keystone = [page[0]];
        for (var index = 1; index < page.Length; index++)
            yield return new BuildLabDecision(
                BuildLabFamily.Rune, (short)(index + 1), keystone, [page[index]], null);
    }

    /// <summary>The spell pair, order-independent: Flash on D and Flash on F are the same choice.</summary>
    public static IEnumerable<BuildLabDecision> Spells(int spell1Id, int spell2Id)
    {
        if (spell1Id <= 0 || spell2Id <= 0)
            yield break;
        yield return new BuildLabDecision(
            BuildLabFamily.Spells, 0, [], [Math.Min(spell1Id, spell2Id), Math.Max(spell1Id, spell2Id)], null);
    }

    /// <summary>
    /// Whether a decision is also counted per lane opponent and per region. Those scopes hold a small
    /// fraction of a champion's games (a lane matchup's median is ~60 per patch). Measured on prod, 75% of
    /// matchup keys for items 1-3 were unique -- one game each -- so anything past the first item is
    /// rows the read side would only ever fall back from.
    /// </summary>
    public static bool CountsInNarrowScopes(BuildLabDecision decision) => decision.Family switch
    {
        BuildLabFamily.Rune => false,
        BuildLabFamily.Item => decision.Stage == 1,
        _ => true
    };

    // An undo reverses the most recent matching acquisition. Tracking the event index of each still
    // active purchase lets the undo mark exactly that purchase, the same replay the timeline follows.
    private static HashSet<int> UndonePurchases(IReadOnlyList<BuildLabItemEvent> ordered)
    {
        var active = new List<(int ItemId, int? EventIndex)>();
        var undone = new HashSet<int>();
        foreach (var itemEvent in ordered)
        {
            switch (itemEvent.EventType)
            {
                case MatchItemEventType.Purchased when itemEvent.ItemId is > 0:
                    active.Add((itemEvent.ItemId.Value, itemEvent.EventIndex));
                    break;
                case MatchItemEventType.Sold or MatchItemEventType.Destroyed when itemEvent.ItemId is > 0:
                    RemoveLast(active, itemEvent.ItemId.Value);
                    break;
                case MatchItemEventType.Undo:
                    if (itemEvent.BeforeId is > 0 &&
                        RemoveLast(active, itemEvent.BeforeId.Value) is { EventIndex: { } index })
                        undone.Add(index);
                    // Undoing a sale hands the item back; it can be sold or undone again later.
                    if (itemEvent.AfterId is > 0)
                        active.Add((itemEvent.AfterId.Value, null));
                    break;
            }
        }
        return undone;
    }

    private static (int ItemId, int? EventIndex)? RemoveLast(List<(int ItemId, int? EventIndex)> active, int itemId)
    {
        for (var index = active.Count - 1; index >= 0; index--)
        {
            if (active[index].ItemId != itemId)
                continue;
            var removed = active[index];
            active.RemoveAt(index);
            return removed;
        }
        return null;
    }
}
