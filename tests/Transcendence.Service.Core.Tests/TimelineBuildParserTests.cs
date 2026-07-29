using FluentAssertions;
using Transcendence.Data.Models.LoL.Match;
using Transcendence.Service.Core.Services.Analytics;
using Transcendence.Service.Core.Services.Jobs;

namespace Transcendence.Service.Core.Tests;

public class TimelineBuildParserTests
{
    private const int DoransBlade = 1055;
    private const int HealthPotion = 2003;
    private const int BfSword = 1038;     // component (builds into Infinity Edge)
    private const int BootsOfSpeed = 1001; // basic boots (component, no BuildsFrom)
    private const int Berserkers = 3006;  // tier-2 boots
    private const int Kraken = 6672;      // completed legendary
    private const int InfinityEdge = 3031; // completed legendary
    private const int Bloodthirster = 3072; // completed legendary
    private const int StealthWard = 3340; // trinket

    private static readonly Dictionary<int, BuildItemMetadata> Items = new()
    {
        [DoransBlade] = new([], [], ["Damage", "LifeSteal"], true, 450),
        [HealthPotion] = new([], [], ["Consumable"], true, 50),
        [BfSword] = new([], [InfinityEdge], ["Damage"], true, 1300),
        [BootsOfSpeed] = new([], [Berserkers], ["Boots"], true, 300),
        [Berserkers] = new([BootsOfSpeed], [], ["Boots", "AttackSpeed"], true, 1100),
        [Kraken] = new([1038, 1018, 1037], [], ["Damage", "AttackSpeed"], true, 3400),
        [InfinityEdge] = new([1038, 1018], [], ["Damage", "CriticalStrike"], true, 3450),
        [Bloodthirster] = new([1038, 1053], [], ["Damage", "LifeSteal"], true, 3400),
        [StealthWard] = new([], [], ["Trinket", "Vision"], true, 0)
    };

    private static BuildItemMetadata? Lookup(int itemId) =>
        Items.TryGetValue(itemId, out var metadata) ? metadata : null;

    private static TimelineBuildEvent Purchase(int participantId, int itemId, long ts) =>
        new(TimelineBuildParser.ItemPurchasedType, participantId, itemId, null, null, ts, null, null);

    private static TimelineBuildEvent Undo(int participantId, int? beforeId, int? afterId, long ts) =>
        new(TimelineBuildParser.ItemUndoType, participantId, null, null, null, ts, beforeId, afterId);

    private static TimelineBuildEvent Sold(int participantId, int itemId, long ts) =>
        new(TimelineBuildParser.ItemSoldType, participantId, itemId, null, null, ts, null, null);

    private static TimelineBuildEvent Destroyed(int participantId, int itemId, long ts) =>
        new(TimelineBuildParser.ItemDestroyedType, participantId, itemId, null, null, ts, null, null);

    private static TimelineBuildEvent Skill(int participantId, int slot, long ts, string? levelUpType = "NORMAL") =>
        new(TimelineBuildParser.SkillLevelUpType, participantId, null, slot, levelUpType, ts, null, null);

    [Fact]
    public void BuildPurchasePaths_OrdersCategorizesAndDropsComponentsAndTrinkets()
    {
        var events = new[]
        {
            Purchase(1, DoransBlade, 0),
            Purchase(1, HealthPotion, 0),
            Purchase(1, StealthWard, 0),     // trinket -> dropped
            Purchase(1, BfSword, 300_000),   // component -> dropped
            Purchase(1, Kraken, 600_000),
            Purchase(1, Berserkers, 700_000),
            Purchase(1, InfinityEdge, 900_000)
        };

        var paths = TimelineBuildParser.BuildPurchasePaths(events, Lookup);

        paths.Should().ContainSingle();
        var path = paths[0];
        path.ParticipantId.Should().Be(1);
        path.Purchases.Select(p => p.ItemId)
            .Should().Equal(DoransBlade, HealthPotion, Kraken, Berserkers, InfinityEdge);
        path.Purchases.Select(p => p.Category).Should().Equal(
            BuildItemCategory.Starter,
            BuildItemCategory.Starter,
            BuildItemCategory.Legendary,
            BuildItemCategory.Boots,
            BuildItemCategory.Legendary);
    }

    [Fact]
    public void BuildPurchasePaths_UndoRemovesThePrecedingPurchase()
    {
        var events = new[]
        {
            Purchase(1, Kraken, 600_000),
            Undo(1, beforeId: Kraken, afterId: 0, 601_000),
            Purchase(1, InfinityEdge, 900_000)
        };

        var paths = TimelineBuildParser.BuildPurchasePaths(events, Lookup);

        paths.Should().ContainSingle();
        paths[0].Purchases.Select(p => p.ItemId).Should().Equal(InfinityEdge);
    }

    [Fact]
    public void BuildItemLifecycle_PreservesComponentsUndoSaleAndDestructionInOrder()
    {
        var events = new[]
        {
            Purchase(1, BfSword, 300_000),
            Purchase(1, Kraken, 600_000),
            Undo(1, beforeId: Kraken, afterId: 0, 601_000),
            Purchase(1, InfinityEdge, 900_000),
            Sold(1, InfinityEdge, 1_000_000),
            Destroyed(1, BfSword, 1_100_000)
        };

        var lifecycle = TimelineBuildParser.BuildItemLifecycle(events.Reverse(), Lookup);

        lifecycle.Select(row => row.EventIndex).Should().Equal(0, 1, 2, 3, 4, 5);
        lifecycle.Select(row => row.EventType).Should().Equal(
            MatchItemEventType.Purchased,
            MatchItemEventType.Purchased,
            MatchItemEventType.Undo,
            MatchItemEventType.Purchased,
            MatchItemEventType.Sold,
            MatchItemEventType.Destroyed);
        lifecycle[0].ItemId.Should().Be(BfSword);
        lifecycle[0].IsBuildRelevant.Should().BeFalse("components remain lossless facts even when they are not public stages");
        lifecycle[2].BeforeId.Should().Be(Kraken);
        lifecycle[2].AfterId.Should().Be(0);
    }

    [Fact]
    public void BuildPurchasePaths_SoldAndDestroyedRemoveItemsFromThePath()
    {
        var events = new[]
        {
            Purchase(1, Kraken, 600_000),
            Purchase(1, InfinityEdge, 900_000),
            Sold(1, Kraken, 1_000_000),               // sold -> removed
            Purchase(1, Bloodthirster, 1_100_000),
            Destroyed(1, Bloodthirster, 1_200_000)    // destroyed -> removed (exercises the destroy path)
        };

        var paths = TimelineBuildParser.BuildPurchasePaths(events, Lookup);

        paths.Should().ContainSingle();
        paths[0].Purchases.Select(p => p.ItemId).Should().Equal(InfinityEdge);
    }

    [Fact]
    public void BuildPurchasePaths_UndoOfSellRestoresTheItem()
    {
        var events = new[]
        {
            Purchase(1, Kraken, 600_000),
            Sold(1, Kraken, 700_000),
            Undo(1, beforeId: 0, afterId: Kraken, 701_000) // undo the sell -> restore
        };

        var paths = TimelineBuildParser.BuildPurchasePaths(events, Lookup);

        paths.Should().ContainSingle();
        paths[0].Purchases.Select(p => p.ItemId).Should().Equal(Kraken);
    }

    [Fact]
    public void BuildPurchasePaths_SeparatesParticipants()
    {
        var events = new[]
        {
            Purchase(1, Kraken, 600_000),
            Purchase(2, InfinityEdge, 600_000)
        };

        var paths = TimelineBuildParser.BuildPurchasePaths(events, Lookup);

        paths.Should().HaveCount(2);
        paths.Single(p => p.ParticipantId == 1).Purchases.Select(x => x.ItemId).Should().Equal(Kraken);
        paths.Single(p => p.ParticipantId == 2).Purchases.Select(x => x.ItemId).Should().Equal(InfinityEdge);
    }

    [Fact]
    public void BuildSkillSequences_DerivesSequenceFirstThreeAndMaxOrder()
    {
        var events = new[]
        {
            Skill(1, 1, 1000), // Q
            Skill(1, 3, 2000), // E
            Skill(1, 2, 3000), // W
            Skill(1, 4, 3500), // R (excluded from first-three + max-order)
            Skill(1, 1, 4000), // Q
            Skill(1, 3, 5000)  // E
        };

        var skills = TimelineBuildParser.BuildSkillSequences(events);

        skills.Should().ContainSingle();
        var skill = skills[0];
        skill.Sequence.Should().Be("Q,E,W,R,Q,E");
        skill.FirstThree.Should().Be("QEW");
        // Q=2 (first idx 0), E=2 (first idx 1), W=1 -> Q>E>W
        skill.MaxOrder.Should().Be("Q>E>W");
    }

    [Fact]
    public void BuildSkillSequences_CollapsesDuplicateLevelUpEvents()
    {
        // Riot's timeline (since patch 15.17) emits duplicate SKILL_LEVEL_UP events with identical
        // (participant, slot, timestamp). Without dedup, slots = [Q,Q,Q,W,Q] would inflate the path.
        var events = new[]
        {
            Skill(1, 1, 1000),
            Skill(1, 1, 1000), // exact duplicate (bug)
            Skill(1, 1, 1000), // exact duplicate (bug)
            Skill(1, 2, 2000), // W
            Skill(1, 1, 3000)  // real second Q point
        };

        var skills = TimelineBuildParser.BuildSkillSequences(events);

        skills.Should().ContainSingle();
        skills[0].Sequence.Should().Be("Q,W,Q");
        skills[0].FirstThree.Should().Be("QWQ");
        skills[0].MaxOrder.Should().Be("Q>W"); // Q=2, W=1 after collapsing the duplicates
    }

    [Fact]
    public void BuildSkillSequences_ExcludesAbilityEvolutions()
    {
        var events = new[]
        {
            Skill(1, 1, 1000, "NORMAL"),
            Skill(1, 2, 2000, "NORMAL"),
            Skill(1, 1, 2500, "EVOLVE"), // not a skill point -> excluded
            Skill(1, 3, 3000, "NORMAL")
        };

        var skills = TimelineBuildParser.BuildSkillSequences(events);

        skills.Should().ContainSingle();
        skills[0].Sequence.Should().Be("Q,W,E");
        skills[0].FirstThree.Should().Be("QWE");
    }
}
