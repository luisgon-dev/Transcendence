using FluentAssertions;
using Transcendence.Service.Core.Services.Analytics.Implementations;

namespace Transcendence.Service.Core.Tests;

public sealed class BuildLabEstimatorTests
{
    private static BuildLabCount Count(string key, short bucket, double games, double wins, double timing = 0) =>
        new(key, bucket, games, wins, timing);

    [Fact]
    public void Estimate_ForAPregameChoice_AdjustedEqualsRaw()
    {
        var cell = BuildLabEstimator.Estimate(
            [Count("4+14", 2, 800, 440), Count("4+12", 2, 200, 90)], parent: null, timed: false);

        cell.Games.Should().Be(1000);
        cell.WinRate.Should().BeApproximately(0.53, 1e-12);
        var flash = cell.Options.Single(option => option.ActionKey == "4+14");
        flash.WinRate.Should().BeApproximately(0.55, 1e-12);
        flash.AdjustedWinRate.Should().BeApproximately(0.55, 1e-12);
        flash.Lift.Should().BeApproximately(0.02, 1e-12);
        flash.PickRate.Should().BeApproximately(0.8, 1e-12);
        flash.ActionIds.Should().Equal(4, 14);
        flash.AverageTimingMinutes.Should().BeNull();
        flash.IsLowSample.Should().BeFalse();
    }

    [Fact]
    public void Estimate_RemovesTheGoldLeadAnOptionIsBoughtWith()
    {
        // Identical within every bucket (70% ahead, 50% even); A is simply bought ahead far more often.
        var cell = BuildLabEstimator.Estimate(
        [
            Count("A", 4, 400, 400 * 0.7), Count("A", 2, 100, 100 * 0.5),
            Count("B", 4, 100, 100 * 0.7), Count("B", 2, 400, 400 * 0.5)
        ], parent: null, timed: false);

        var a = cell.Options.Single(option => option.ActionKey == "A");
        var b = cell.Options.Single(option => option.ActionKey == "B");
        (a.WinRate - b.WinRate).Should().BeApproximately(0.12, 1e-9, "raw rates carry the lead");
        a.AdjustedWinRate.Should().BeApproximately(b.AdjustedWinRate, 1e-9);
        a.Lift.Should().BeApproximately(0, 1e-9);
    }

    [Fact]
    public void Estimate_KeepsAGenuineWithinBucketAdvantage()
    {
        // A beats B by 10pp inside each bucket and is also bought ahead more often.
        var cell = BuildLabEstimator.Estimate(
        [
            Count("A", 4, 400, 400 * 0.75), Count("A", 2, 100, 100 * 0.55),
            Count("B", 4, 100, 100 * 0.65), Count("B", 2, 400, 400 * 0.45)
        ], parent: null, timed: false);

        var a = cell.Options.Single(option => option.ActionKey == "A");
        var b = cell.Options.Single(option => option.ActionKey == "B");
        var adjustedGap = a.AdjustedWinRate - b.AdjustedWinRate;
        adjustedGap.Should().BePositive();
        adjustedGap.Should().BeLessThan(a.WinRate - b.WinRate, "the lead's share of the raw gap is removed");
    }

    [Fact]
    public void Estimate_ShrinksAThinScopeTowardTheAllGamesLift()
    {
        var all = BuildLabEstimator.Estimate(
            [Count("A", 2, 10_000, 5_000), Count("B", 2, 10_000, 5_000)], parent: null, timed: false);
        var matchup = BuildLabEstimator.Estimate(
            [Count("A", 2, 20, 18), Count("B", 2, 20, 2)], all, timed: false);

        var a = matchup.Options.Single(option => option.ActionKey == "A");
        // Raw lift +0.4 on 20 games, pulled toward the all-games lift of 0 with 100 pseudo-games.
        a.Lift.Should().BeApproximately(0.4 * 20 / 120, 1e-9);
        a.WinRate.Should().Be(0.9, "the raw rate is what happened and is never shrunk");
    }

    [Fact]
    public void Estimate_GivesAnUndefeatedOptionANonZeroInterval()
    {
        var option = BuildLabEstimator.Estimate([Count("A", 2, 8, 8)], parent: null, timed: false)
            .Options.Single();

        (option.ConfidenceHigh - option.ConfidenceLow).Should().BeGreaterThan(0.2);
        option.IsLowSample.Should().BeTrue();
    }

    [Fact]
    public void Estimate_ReportsAverageTimingForTimedDecisions()
    {
        var option = BuildLabEstimator.Estimate(
            [Count("3031", 2, 2, 1, timing: 2 * 900)], parent: null, timed: true).Options.Single();

        option.AverageTimingMinutes.Should().Be(15);
    }

    [Fact]
    public void Rank_PutsLowSampleOptionsLastInEveryMode()
    {
        var cell = BuildLabEstimator.Estimate(
            [Count("rare", 2, 20, 20), Count("solid", 2, 900, 480), Count("weak", 2, 900, 400)],
            parent: null, timed: false);

        foreach (var mode in new[] { "SUPPORTED", "IMPACT", "COMMON" })
            BuildLabEstimator.Rank(cell.Options, mode).Last().ActionKey.Should().Be("rare", mode);
        BuildLabEstimator.Rank(cell.Options, "SUPPORTED").First().ActionKey.Should().Be("solid");
    }
}
