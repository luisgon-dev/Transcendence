using FluentAssertions;
using Transcendence.Data.Models.LoL.Analytics;
using Transcendence.Data.Models.LoL.Match;
using Transcendence.Service.Core.Services.Analytics.Implementations;

namespace Transcendence.Service.Core.Tests;

public sealed class BuildLabDecisionsTests
{
    private static int eventIndex;

    private static BuildLabItemEvent Buy(int itemId, int atSeconds, BuildItemCategory? category) =>
        new(++eventIndex, MatchItemEventType.Purchased, atSeconds * 1000, itemId, null, null, category);

    private static BuildLabItemEvent Undo(int itemId, int atSeconds) =>
        new(++eventIndex, MatchItemEventType.Undo, atSeconds * 1000, null, itemId, null, null);

    private static BuildLabItemEvent Sell(int itemId, int atSeconds) =>
        new(++eventIndex, MatchItemEventType.Sold, atSeconds * 1000, itemId, null, null, null);

    [Fact]
    public void Items_EmitsStarterSetThenEachLegendaryConditionedOnTheOnesBefore()
    {
        var decisions = BuildLabDecisions.Items(
        [
            Buy(2003, 5, BuildItemCategory.Starter),
            Buy(1055, 6, BuildItemCategory.Starter),
            Buy(3006, 500, BuildItemCategory.Boots),
            Buy(6672, 700, BuildItemCategory.Legendary),
            Buy(3031, 1100, BuildItemCategory.Legendary),
            Buy(3036, 1500, BuildItemCategory.Legendary)
        ]).ToList();

        decisions.Select(d => (d.Family, d.Stage, string.Join(",", d.Prefix), d.ActionKey)).Should().Equal(
            (BuildLabFamily.Starter, (short)0, "", "1055+2003"),
            (BuildLabFamily.Boots, (short)1, "", "3006"),
            (BuildLabFamily.Item, (short)1, "", "6672"),
            (BuildLabFamily.Item, (short)2, "6672", "3031"),
            (BuildLabFamily.Item, (short)3, "6672,3031", "3036"));
        decisions.Single(d => d.Family == BuildLabFamily.Item && d.Stage == 2).TimestampMs.Should().Be(1_100_000);
    }

    [Fact]
    public void Items_IgnoresLaterPotionsAndSecondBootsAndUndonePurchases()
    {
        var decisions = BuildLabDecisions.Items(
        [
            Buy(1056, 3, BuildItemCategory.Starter),
            Buy(2003, 900, BuildItemCategory.Starter), // a refill, not part of the start
            Buy(3020, 400, BuildItemCategory.Boots),
            Buy(3158, 1300, BuildItemCategory.Boots), // boots swap later
            Buy(3089, 600, BuildItemCategory.Legendary),
            Undo(3089, 602),
            Buy(6655, 650, BuildItemCategory.Legendary)
        ]).ToList();

        decisions.Single(d => d.Family == BuildLabFamily.Starter).ActionKey.Should().Be("1056");
        decisions.Single(d => d.Family == BuildLabFamily.Boots).ActionKey.Should().Be("3020");
        decisions.Where(d => d.Family == BuildLabFamily.Item).Select(d => d.ActionKey)
            .Should().Equal("6655");
    }

    [Fact]
    public void Items_DoesNotCountABoughtBackLegendaryAsANewDecision()
    {
        var decisions = BuildLabDecisions.Items(
        [
            Buy(3031, 700, BuildItemCategory.Legendary),
            Sell(3031, 1500),
            Buy(3031, 1600, BuildItemCategory.Legendary),
            Buy(3036, 1900, BuildItemCategory.Legendary)
        ]).ToList();

        decisions.Select(d => (d.Stage, d.ActionKey)).Should().Equal(((short)1, "3031"), ((short)2, "3036"));
    }

    [Fact]
    public void Items_StopsAfterSixLegendaries()
    {
        var events = Enumerable.Range(0, 8)
            .Select(offset => Buy(3000 + offset, 600 + offset * 100, BuildItemCategory.Legendary));

        BuildLabDecisions.Items(events).Max(d => d.Stage).Should().Be(BuildLabDecisions.MaximumItemStage);
    }

    [Fact]
    public void Items_UndoOfASaleRestoresTheItemSoALaterUndoFindsIt()
    {
        // Buy, sell, undo the sale (item comes back), then undo again: the second undo reverses the
        // restored item rather than reaching back to the original purchase.
        var decisions = BuildLabDecisions.Items(
        [
            Buy(3031, 700, BuildItemCategory.Legendary),
            Sell(3031, 800),
            new BuildLabItemEvent(++eventIndex, MatchItemEventType.Undo, 801_000, null, null, 3031, null),
            Undo(3031, 802)
        ]).ToList();

        decisions.Select(d => d.ActionKey).Should().Equal("3031");
    }

    [Fact]
    public void Runes_EmitsThePageThenEachSlotConditionedOnTheKeystone()
    {
        var decisions = BuildLabDecisions.Runes(
        [
            new BuildLabRune(RuneSelectionTree.Secondary, 0, 8304),
            new BuildLabRune(RuneSelectionTree.Primary, 1, 8143),
            new BuildLabRune(RuneSelectionTree.Primary, 0, 8112)
        ]).ToList();

        decisions.Select(d => (d.Family, d.Stage, string.Join(",", d.Prefix), d.ActionKey)).Should().Equal(
            (BuildLabFamily.RunePage, (short)0, "", "8112+8143+8304"),
            (BuildLabFamily.Rune, (short)1, "", "8112"),
            (BuildLabFamily.Rune, (short)2, "8112", "8143"),
            (BuildLabFamily.Rune, (short)3, "8112", "8304"));
    }

    [Fact]
    public void Spells_AreOrderIndependent()
    {
        BuildLabDecisions.Spells(14, 4).Single().ActionKey.Should().Be("4+14");
        BuildLabDecisions.Spells(4, 14).Single().ActionKey.Should().Be("4+14");
        BuildLabDecisions.Spells(4, 0).Should().BeEmpty();
    }

    [Theory]
    [InlineData(null, 2)]
    [InlineData(-4000, 0)]
    [InlineData(-2500, 1)]
    [InlineData(-751, 1)]
    [InlineData(0, 2)]
    [InlineData(750, 3)]
    [InlineData(2499, 3)]
    [InlineData(2500, 4)]
    public void GoldBucket_SplitsTeamGoldDifferenceIntoFiveBands(int? difference, short expected) =>
        BuildLabDecisions.GoldBucket(difference).Should().Be(expected);

    [Fact]
    public void PathHash_IsOrderSensitiveAndStable()
    {
        BuildLabPath.Hash([3031, 3036]).Should().NotBe(BuildLabPath.Hash([3036, 3031]));
        BuildLabPath.Hash([]).Should().Be(BuildLabPath.Hash(Array.Empty<int>()));
        // Pinned: the refresher writes this value and the service looks it up, so it must never drift.
        BuildLabPath.Hash([]).Should().Be(unchecked((long)0xE3B0C44298FC1C14UL));
    }

    [Fact]
    public void NarrowScopes_KeepEarlyChoicesAndDropDeepPaths()
    {
        BuildLabDecisions.CountsInNarrowScopes(new(BuildLabFamily.Item, 1, [], [3], 0)).Should().BeTrue();
        BuildLabDecisions.CountsInNarrowScopes(new(BuildLabFamily.Item, 2, [1], [4], 0)).Should().BeFalse();
        BuildLabDecisions.CountsInNarrowScopes(new(BuildLabFamily.Rune, 1, [], [8112], null)).Should().BeFalse();
        BuildLabDecisions.CountsInNarrowScopes(new(BuildLabFamily.RunePage, 0, [], [8112], null)).Should().BeTrue();
    }
}
