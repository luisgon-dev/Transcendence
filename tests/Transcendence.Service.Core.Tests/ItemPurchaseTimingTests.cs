using FluentAssertions;
using Transcendence.Data.Models.LoL.Analytics;
using Transcendence.Service.Core.Services.Analytics.Implementations;
using Transcendence.Service.Core.Services.Analytics.Models;

namespace Transcendence.Service.Core.Tests;

public sealed class ItemPurchaseTimingTests
{
    private static ItemPurchaseCount Count(
        short stage, short bucket, int games, int wins, long timingSeconds = 0,
        BuildLabFamily family = BuildLabFamily.Item) =>
        new(family, stage, bucket, games, wins, timingSeconds);

    [Fact]
    public void Estimate_JudgesAStageAgainstEveryItemFinishedThereInTheSameGoldState()
    {
        // The item is only finished far ahead, where every item wins 70%: its raw rate is the lead's.
        ItemPurchaseCount[] item = [Count(2, 4, 10_000, 7_000, 10_000L * 1_140)];
        ItemPurchaseCount[] population =
        [
            Count(2, 4, 20_000, 14_000),
            Count(2, 0, 10_000, 3_000)
        ];

        var timing = ItemPurchaseTiming.Estimate(3031, "16.20", "ALL", item, population)!;

        timing.Kind.Should().Be("ITEM");
        timing.Games.Should().Be(10_000);
        timing.AverageMinutes.Should().BeApproximately(19, 1e-9);
        var stage = timing.Stages.Should().ContainSingle().Subject;
        stage.Stage.Should().Be(2);
        stage.WinRate.Should().BeApproximately(0.7, 1e-12);
        stage.StageWinRate.Should().BeApproximately(17_000.0 / 30_000, 1e-12);
        // No lift once the lead is accounted for, so it sits at the stage's own rate.
        stage.AdjustedWinRate.Should().BeApproximately(stage.StageWinRate, 1e-12);
        stage.ShareOfStage.Should().BeApproximately(1.0 / 3, 1e-12);
    }

    [Fact]
    public void Estimate_OrdersStages_ReportsBoots_AndNeverCountsNegativeOtherGames()
    {
        ItemPurchaseCount[] item =
        [
            Count(1, 2, 50, 30, 50L * 540, BuildLabFamily.Boots),
            Count(1, 3, 40, 20, 40L * 600, BuildLabFamily.Boots)
        ];
        // The population read trails the item's in bucket 3, as when the refresher added between them.
        ItemPurchaseCount[] population =
        [
            Count(1, 2, 500, 250, family: BuildLabFamily.Boots),
            Count(1, 3, 30, 15, family: BuildLabFamily.Boots),
            Count(1, 2, 9_999, 9_999)
        ];

        var timing = ItemPurchaseTiming.Estimate(3006, "16.20", "KR", item, population)!;

        timing.Kind.Should().Be("BOOTS");
        timing.Region.Should().Be("KR");
        var stage = timing.Stages.Should().ContainSingle().Subject;
        stage.Games.Should().Be(90);
        stage.IsLowSample.Should().BeTrue();
        // 450 other boots in bucket 2, none in bucket 3: legendary counts never enter the boots stage.
        stage.ShareOfStage.Should().BeApproximately(90.0 / 540, 1e-12);
        stage.AverageMinutes.Should().BeApproximately((50 * 9.0 + 40 * 10.0) / 90, 1e-9);
    }

    [Fact]
    public void Estimate_WithoutCounts_ReturnsNothing()
    {
        ItemPurchaseTiming.Estimate(3031, "16.20", "ALL", [], [Count(1, 2, 10, 5)]).Should().BeNull();
    }
}
