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

public enum OpeningBuyRejection
{
    None,
    /// <summary>Nothing priced was bought in the opening window.</summary>
    Empty,
    /// <summary>The set costs more than the starting gold, so it is not what the player started with.</summary>
    OverBudget,
    /// <summary>An item has no price on the patch, so the budget cannot be checked.</summary>
    UnknownPrice
}

public sealed record OpeningBuyResult(BuildLabDecision? Decision, OpeningBuyRejection Rejection);

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
    /// Purchases before this are the opening buy. Minions and passive gold arrive at 1:05, so nothing
    /// bought earlier can be paid for with anything but starting gold; the old 90s window let a
    /// base-and-return at 1:19 (Cloth Armor from invade gold, a potion after using two) into the start.
    /// </summary>
    public const int OpeningWindowMs = 60_000;

    /// <summary>
    /// The gold every Summoner's Rift player starts with. An opening buy that costs more is not an
    /// opening buy -- it is a replay that missed a sell or merged a second shop -- and is never counted.
    /// </summary>
    public const int StartingGold = 500;

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
    /// Replays one participant's item lifecycle into the in-game item decisions they made: each
    /// completed legendary and the first boots. The opening buy is <see cref="OpeningBuy"/>.
    ///
    /// An undo removes the purchase it reverses, so an item bought and immediately refunded never
    /// counts. A legendary acquired a second time (sold, then bought back) is not a new decision.
    /// </summary>
    public static IEnumerable<BuildLabDecision> Items(IEnumerable<BuildLabItemEvent> events)
    {
        var ordered = Order(events);
        var undone = UndonePurchases(ordered);

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
        return decisions;
    }

    /// <summary>
    /// What the participant left the fountain with: every priced item bought before
    /// <see cref="OpeningWindowMs"/>, net of undos AND sells. Selling refunds an item in full at the
    /// start, so "buy Amplifying Tome, sell it, buy Doran's Ring" is a Doran's Ring start -- counting
    /// both is how a 900g "starter" appeared.
    ///
    /// Every item counts, not only starter-category ones: a Long Sword start is a start. Free items
    /// (the trinket) carry no information and are left out.
    ///
    /// The result is then checked against the only rule the game enforces, <see cref="StartingGold"/>.
    /// A set that costs more, or holds an item with no known price on the patch, is rejected rather
    /// than counted: whatever the replay got wrong, an impossible start can never reach the table.
    /// </summary>
    public static OpeningBuyResult OpeningBuy(
        IEnumerable<BuildLabItemEvent> events,
        IReadOnlyDictionary<int, int> prices)
    {
        var ordered = Order(events);
        var undone = UndonePurchases(ordered);
        var held = new List<int>();
        foreach (var itemEvent in ordered)
        {
            if (itemEvent.TimestampMs >= OpeningWindowMs)
                break;
            switch (itemEvent.EventType)
            {
                case MatchItemEventType.Purchased
                    when itemEvent.ItemId is > 0 && !undone.Contains(itemEvent.EventIndex):
                    held.Add(itemEvent.ItemId.Value);
                    break;
                case MatchItemEventType.Sold when itemEvent.ItemId is > 0:
                    held.Remove(itemEvent.ItemId.Value);
                    break;
                // Undoing a sale hands the item back.
                case MatchItemEventType.Undo when itemEvent.AfterId is > 0:
                    held.Add(itemEvent.AfterId.Value);
                    break;
            }
        }

        if (held.Any(id => !prices.ContainsKey(id)))
            return new OpeningBuyResult(null, OpeningBuyRejection.UnknownPrice);
        held.RemoveAll(id => prices[id] <= 0);
        if (held.Count == 0)
            return new OpeningBuyResult(null, OpeningBuyRejection.Empty);
        if (held.Sum(id => prices[id]) > StartingGold)
            return new OpeningBuyResult(null, OpeningBuyRejection.OverBudget);

        held.Sort();
        return new OpeningBuyResult(
            new BuildLabDecision(BuildLabFamily.Starter, 0, [], held, 0), OpeningBuyRejection.None);
    }

    private static List<BuildLabItemEvent> Order(IEnumerable<BuildLabItemEvent> events) =>
        events
            .OrderBy(itemEvent => itemEvent.TimestampMs)
            .ThenBy(itemEvent => itemEvent.EventIndex)
            .ToList();

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
