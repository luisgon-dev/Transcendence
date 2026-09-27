using FluentAssertions;
using Microsoft.Extensions.Options;
using Transcendence.Service.Core.Services.Jobs.Configuration;
using Transcendence.Service.Core.Services.Jobs.Priority;

namespace Transcendence.Service.Core.Tests;

/// <summary>
/// The discovery producers stop counting a region's pending summoners at
/// <see cref="AdaptiveThroughputBudgetPolicy.SaturatingPendingCandidateCount"/> instead of counting the
/// whole table. That is only sound if the budget cannot tell a count at the cap from any larger one.
/// </summary>
public class AdaptiveThroughputBudgetSaturationTests
{
    public static TheoryData<double, int, int, int, bool> Situations() => new()
    {
        // pressure threshold, successful, target, recent, backlog-old
        { 1.1, 50, 1000, 0, false },
        { 1.1, 50, 1000, 500, false },
        { 1.1, 5000, 1000, 0, false },
        { 1.1, 5000, 1000, 500, true },
        { 3.5, 900, 1000, 0, false },
        { 3.5, 900, 1000, 200, false },
    };

    [Theory]
    [MemberData(nameof(Situations))]
    public void Budget_DecidesTheSameAtTheCapAsForAnyLargerCount(
        double threshold, int successful, int target, int recent, bool backlogOld)
    {
        var policy = new AdaptiveThroughputBudgetPolicy(Options.Create(new AdaptiveThroughputBudgetOptions
        {
            CatchUpCandidatePressureThreshold = threshold
        }));
        const int baseline = 250;
        var cap = policy.SaturatingPendingCandidateCount(baseline);
        var now = new DateTime(2026, 9, 27, 12, 0, 0, DateTimeKind.Utc);

        AdaptiveThroughputBudgetDecision Decide(int pending) => policy.ComputeBudget(new(
            // A fresh key per call: the hysteresis state must not carry between the compared decisions.
            $"producer-{Guid.NewGuid():N}", now, false, successful, target,
            backlogOld ? now.AddHours(-12) : now.AddMinutes(-1), recent, pending, baseline, 5, 50));

        var atCap = Decide(cap);
        foreach (var larger in new[] { cap + 1, cap * 10, 680_000, int.MaxValue / 2 })
        {
            var decision = Decide(larger);
            decision.Mode.Should().Be(atCap.Mode);
            decision.QueueTarget.Should().Be(atCap.QueueTarget);
            decision.MaxCandidates.Should().Be(atCap.MaxCandidates);
            decision.IncludeAllModes.Should().Be(atCap.IncludeAllModes);
        }

        // And the cap is a real bound: below it the count can still change the decision somewhere.
        cap.Should().BeGreaterThanOrEqualTo((int)Math.Ceiling(baseline * Math.Max(2d, threshold)));
    }
}
