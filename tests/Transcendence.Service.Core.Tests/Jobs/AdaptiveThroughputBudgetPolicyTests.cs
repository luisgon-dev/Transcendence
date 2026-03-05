using FluentAssertions;
using Microsoft.Extensions.Options;
using Transcendence.Service.Core.Services.Jobs.Configuration;
using Transcendence.Service.Core.Services.Jobs.Priority;

namespace Transcendence.Service.Core.Tests.Jobs;

public class AdaptiveThroughputBudgetPolicyTests
{
    [Fact]
    public void ComputeBudget_TransitionsFromHighPressureToBalancedAfterCooldown()
    {
        var policy = CreatePolicy(new AdaptiveThroughputBudgetOptions
        {
            HighPressureCooldownMinutes = 5,
            CatchUpHoldMinutes = 1,
            ModeSwitchCooldownMinutes = 0,
            CatchUpCoverageThreshold = 0.8d,
            CatchUpBacklogAgeMinutes = 120,
            CatchUpCandidatePressureThreshold = 2d,
            MinimumRecentVelocityPerHour = 1d
        });

        var now = new DateTime(2026, 3, 5, 19, 10, 0, DateTimeKind.Utc);
        var highPressure = policy.ComputeBudget(CreateInput(now, isApiPriorityDemandActive: true));
        var withinCooldown = policy.ComputeBudget(CreateInput(now.AddMinutes(2), isApiPriorityDemandActive: false));
        var afterCooldown = policy.ComputeBudget(CreateInput(now.AddMinutes(6), isApiPriorityDemandActive: false));

        highPressure.Mode.Should().Be(AdaptiveThroughputBudgetMode.HighPressure);
        highPressure.QueueTarget.Should().Be(0);
        withinCooldown.Mode.Should().Be(AdaptiveThroughputBudgetMode.HighPressure);
        withinCooldown.QueueTarget.Should().Be(0);
        afterCooldown.Mode.Should().Be(AdaptiveThroughputBudgetMode.Balanced);
        afterCooldown.QueueTarget.Should().BeGreaterThan(0);
    }

    [Fact]
    public void ComputeBudget_WhenPressureReenters_PrefersImmediateHighPressureMode()
    {
        var policy = CreatePolicy(new AdaptiveThroughputBudgetOptions
        {
            HighPressureCooldownMinutes = 6,
            CatchUpHoldMinutes = 3,
            ModeSwitchCooldownMinutes = 2,
            CatchUpCoverageThreshold = 0.75d,
            CatchUpBacklogAgeMinutes = 120,
            CatchUpCandidatePressureThreshold = 2d,
            MinimumRecentVelocityPerHour = 1d
        });

        var now = new DateTime(2026, 3, 5, 19, 20, 0, DateTimeKind.Utc);
        var balanced = policy.ComputeBudget(CreateInput(now, isApiPriorityDemandActive: false));
        var highPressure = policy.ComputeBudget(CreateInput(now.AddMinutes(1), isApiPriorityDemandActive: true));
        var cooling = policy.ComputeBudget(CreateInput(now.AddMinutes(2), isApiPriorityDemandActive: false));
        var reentry = policy.ComputeBudget(CreateInput(now.AddMinutes(3), isApiPriorityDemandActive: true));

        balanced.Mode.Should().Be(AdaptiveThroughputBudgetMode.Balanced);
        highPressure.Mode.Should().Be(AdaptiveThroughputBudgetMode.HighPressure);
        cooling.Mode.Should().Be(AdaptiveThroughputBudgetMode.HighPressure);
        reentry.Mode.Should().Be(AdaptiveThroughputBudgetMode.HighPressure);
        reentry.QueueTarget.Should().Be(0);
    }

    [Fact]
    public void ComputeBudget_BoundaryInputsProduceStableClampedOutputs()
    {
        var policy = CreatePolicy(new AdaptiveThroughputBudgetOptions
        {
            VelocityLookbackMinutes = 30,
            ModeSwitchCooldownMinutes = 0,
            CatchUpCoverageThreshold = 0.9d,
            CatchUpBacklogAgeMinutes = 45,
            CatchUpQueueBurstMultiplier = 2.5d,
            CatchUpCandidateBurstMultiplier = 3d,
            MaxQueueTargetHardCap = 12,
            MaxCandidateHardCap = 40
        });

        var now = new DateTime(2026, 3, 5, 19, 30, 0, DateTimeKind.Utc);
        var first = policy.ComputeBudget(new AdaptiveThroughputBudgetInput(
            ProducerKey: "boundary-producer",
            EvaluationUtc: now,
            IsApiPriorityDemandActive: false,
            SuccessfulMatchesForPatch: -10,
            TargetSuccessfulMatchesForPatch: 0,
            LatestPatchSuccessFetchUtc: null,
            RecentSuccessfulMatches: -4,
            PendingCandidateCount: -2,
            BaselineMaxCandidates: 0,
            BaselineMinQueueTarget: -3,
            BaselineMaxQueueTarget: 0));
        var second = policy.ComputeBudget(new AdaptiveThroughputBudgetInput(
            ProducerKey: "boundary-producer",
            EvaluationUtc: now.AddMinutes(1),
            IsApiPriorityDemandActive: false,
            SuccessfulMatchesForPatch: -10,
            TargetSuccessfulMatchesForPatch: 0,
            LatestPatchSuccessFetchUtc: null,
            RecentSuccessfulMatches: -4,
            PendingCandidateCount: -2,
            BaselineMaxCandidates: 0,
            BaselineMinQueueTarget: -3,
            BaselineMaxQueueTarget: 0));

        first.Mode.Should().Be(AdaptiveThroughputBudgetMode.CatchUp);
        first.QueueTarget.Should().BeInRange(1, 12);
        first.MaxCandidates.Should().BeInRange(1, 40);
        first.IncludeAllModes.Should().BeFalse();

        second.Mode.Should().Be(first.Mode);
        second.QueueTarget.Should().Be(first.QueueTarget);
        second.MaxCandidates.Should().Be(first.MaxCandidates);
    }

    private static AdaptiveThroughputBudgetPolicy CreatePolicy(AdaptiveThroughputBudgetOptions options) =>
        new(Options.Create(options));

    private static AdaptiveThroughputBudgetInput CreateInput(DateTime evaluationUtc, bool isApiPriorityDemandActive) =>
        new(
            ProducerKey: "champion-ingestion",
            EvaluationUtc: evaluationUtc,
            IsApiPriorityDemandActive: isApiPriorityDemandActive,
            SuccessfulMatchesForPatch: 120,
            TargetSuccessfulMatchesForPatch: 100,
            LatestPatchSuccessFetchUtc: evaluationUtc.AddMinutes(-10),
            RecentSuccessfulMatches: 40,
            PendingCandidateCount: 12,
            BaselineMaxCandidates: 50,
            BaselineMinQueueTarget: 2,
            BaselineMaxQueueTarget: 8);
}
