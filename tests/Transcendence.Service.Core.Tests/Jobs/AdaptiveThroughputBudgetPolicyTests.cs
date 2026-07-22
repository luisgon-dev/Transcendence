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

    [Fact]
    public void ComputeBudget_TransitionsIntoCatchUpUnderSpikePressure_ThenReturnsBalanced()
    {
        var policy = CreatePolicy(new AdaptiveThroughputBudgetOptions
        {
            VelocityLookbackMinutes = 30,
            ModeSwitchCooldownMinutes = 0,
            HighPressureCooldownMinutes = 0,
            CatchUpHoldMinutes = 3,
            CatchUpCoverageThreshold = 0.85d,
            CatchUpBacklogAgeMinutes = 45,
            CatchUpCandidatePressureThreshold = 1.1d,
            MinimumRecentVelocityPerHour = 12d,
            CatchUpQueueBurstMultiplier = 2d,
            CatchUpCandidateBurstMultiplier = 2d,
            MaxQueueTargetHardCap = 20,
            MaxCandidateHardCap = 120
        });

        var now = new DateTime(2026, 3, 5, 19, 40, 0, DateTimeKind.Utc);
        var spikeCatchUp = policy.ComputeBudget(CreateInput(
            now,
            isApiPriorityDemandActive: false,
            successfulMatchesForPatch: 60,
            targetSuccessfulMatchesForPatch: 100,
            latestPatchSuccessFetchUtc: now.AddMinutes(-120),
            recentSuccessfulMatches: 2,
            pendingCandidateCount: 150,
            baselineMaxCandidates: 50,
            baselineMinQueueTarget: 2,
            baselineMaxQueueTarget: 6));
        var holdCatchUp = policy.ComputeBudget(CreateInput(
            now.AddMinutes(1),
            isApiPriorityDemandActive: false,
            successfulMatchesForPatch: 100,
            targetSuccessfulMatchesForPatch: 100,
            latestPatchSuccessFetchUtc: now.AddMinutes(-2),
            recentSuccessfulMatches: 60,
            pendingCandidateCount: 20,
            baselineMaxCandidates: 50,
            baselineMinQueueTarget: 2,
            baselineMaxQueueTarget: 6));
        var backToBalanced = policy.ComputeBudget(CreateInput(
            now.AddMinutes(4),
            isApiPriorityDemandActive: false,
            successfulMatchesForPatch: 100,
            targetSuccessfulMatchesForPatch: 100,
            latestPatchSuccessFetchUtc: now.AddMinutes(-2),
            recentSuccessfulMatches: 60,
            pendingCandidateCount: 20,
            baselineMaxCandidates: 50,
            baselineMinQueueTarget: 2,
            baselineMaxQueueTarget: 6));

        spikeCatchUp.Mode.Should().Be(AdaptiveThroughputBudgetMode.CatchUp);
        spikeCatchUp.QueueTarget.Should().BeGreaterThan(6);
        holdCatchUp.Mode.Should().Be(AdaptiveThroughputBudgetMode.CatchUp);
        backToBalanced.Mode.Should().Be(AdaptiveThroughputBudgetMode.Balanced);
        backToBalanced.QueueTarget.Should().BeInRange(2, 6);
    }

    [Fact]
    public async Task ComputeBudget_ConcurrentTransitionsForOneProducerRemainValid()
    {
        var policy = CreatePolicy(new AdaptiveThroughputBudgetOptions
        {
            ModeSwitchCooldownMinutes = 0,
            HighPressureCooldownMinutes = 0,
            CatchUpHoldMinutes = 0,
            CatchUpCoverageThreshold = 0.8d,
            CatchUpBacklogAgeMinutes = 120
        });
        var now = new DateTime(2026, 3, 5, 20, 0, 0, DateTimeKind.Utc);

        var decisions = await Task.WhenAll(Enumerable.Range(0, 100).Select(index => Task.Run(() =>
            policy.ComputeBudget(CreateInput(
                now.AddSeconds(index),
                isApiPriorityDemandActive: index % 2 == 0)))));

        decisions.Should().HaveCount(100);
        decisions.All(decision =>
            (decision.Mode is AdaptiveThroughputBudgetMode.HighPressure or AdaptiveThroughputBudgetMode.Balanced) &&
            decision.QueueTarget >= 0).Should().BeTrue();
    }

    private static AdaptiveThroughputBudgetPolicy CreatePolicy(AdaptiveThroughputBudgetOptions options) =>
        new(Options.Create(options));

    private static AdaptiveThroughputBudgetInput CreateInput(
        DateTime evaluationUtc,
        bool isApiPriorityDemandActive,
        int successfulMatchesForPatch = 120,
        int targetSuccessfulMatchesForPatch = 100,
        DateTime? latestPatchSuccessFetchUtc = null,
        int recentSuccessfulMatches = 40,
        int pendingCandidateCount = 12,
        int baselineMaxCandidates = 50,
        int baselineMinQueueTarget = 2,
        int baselineMaxQueueTarget = 8) =>
        new(
            ProducerKey: "champion-ingestion",
            EvaluationUtc: evaluationUtc,
            IsApiPriorityDemandActive: isApiPriorityDemandActive,
            SuccessfulMatchesForPatch: successfulMatchesForPatch,
            TargetSuccessfulMatchesForPatch: targetSuccessfulMatchesForPatch,
            LatestPatchSuccessFetchUtc: latestPatchSuccessFetchUtc ?? evaluationUtc.AddMinutes(-10),
            RecentSuccessfulMatches: recentSuccessfulMatches,
            PendingCandidateCount: pendingCandidateCount,
            BaselineMaxCandidates: baselineMaxCandidates,
            BaselineMinQueueTarget: baselineMinQueueTarget,
            BaselineMaxQueueTarget: baselineMaxQueueTarget);
}
