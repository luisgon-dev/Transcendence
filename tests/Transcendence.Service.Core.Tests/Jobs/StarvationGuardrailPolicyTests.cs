using FluentAssertions;
using Microsoft.Extensions.Options;
using Transcendence.Service.Core.Services.Jobs.Configuration;
using Transcendence.Service.Core.Services.Jobs.Priority;

namespace Transcendence.Service.Core.Tests.Jobs;

public class StarvationGuardrailPolicyTests
{
    [Fact]
    public void Evaluate_WhenDeferAgeBreachesThreshold_StartsForcedCatchUpWindow()
    {
        var policy = CreatePolicy(new StarvationGuardrailOptions
        {
            Enabled = true,
            MaxEligibleDeferAgeMinutes = 120,
            CatchUpWindowMinutes = 10,
            CatchUpCooldownMinutes = 30,
            ForcedCatchUpQueueTargetFloor = 2,
            ForcedCatchUpCandidateBurstMultiplier = 2d,
            ForcedCatchUpMaxCandidateHardCap = 50
        });

        var decision = policy.Evaluate(CreateInput(
            maxEligibleDeferAgeMinutes: 180,
            isCatchUpWindowActive: false,
            isCatchUpCooldownActive: false,
            baselineQueueTarget: 0,
            baselineMaxCandidates: 8));

        decision.Outcome.Should().Be(StarvationGuardrailOutcome.CatchUpWindowStart);
        decision.ShouldStartCatchUpWindow.Should().BeTrue();
        decision.IsForcedCatchUpActive.Should().BeTrue();
        decision.QueueTarget.Should().Be(2);
        decision.MaxCandidates.Should().Be(16);
        decision.CatchUpWindowTtl.Should().Be(TimeSpan.FromMinutes(10));
        decision.CatchUpCooldownTtl.Should().Be(TimeSpan.FromMinutes(30));
    }

    [Fact]
    public void Evaluate_WhenCatchUpWindowAlreadyActive_ContinuesForcedCatchUp()
    {
        var policy = CreatePolicy(new StarvationGuardrailOptions
        {
            Enabled = true,
            MaxEligibleDeferAgeMinutes = 90,
            ForcedCatchUpQueueTargetFloor = 1,
            ForcedCatchUpCandidateBurstMultiplier = 1.5d,
            ForcedCatchUpMaxCandidateHardCap = 25
        });

        var decision = policy.Evaluate(CreateInput(
            maxEligibleDeferAgeMinutes: 60,
            isCatchUpWindowActive: true,
            isCatchUpCooldownActive: true,
            baselineQueueTarget: 1,
            baselineMaxCandidates: 10));

        decision.Outcome.Should().Be(StarvationGuardrailOutcome.CatchUpWindowContinue);
        decision.ShouldStartCatchUpWindow.Should().BeFalse();
        decision.IsForcedCatchUpActive.Should().BeTrue();
        decision.QueueTarget.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public void Evaluate_WhenWindowExpiredAndCooldownActive_DoesNotRestartCatchUp()
    {
        var policy = CreatePolicy(new StarvationGuardrailOptions
        {
            Enabled = true,
            MaxEligibleDeferAgeMinutes = 90,
            ForcedCatchUpQueueTargetFloor = 2
        });

        var decision = policy.Evaluate(CreateInput(
            maxEligibleDeferAgeMinutes: 300,
            isCatchUpWindowActive: false,
            isCatchUpCooldownActive: true,
            baselineQueueTarget: 0,
            baselineMaxCandidates: 12));

        decision.Outcome.Should().Be(StarvationGuardrailOutcome.CatchUpCooldown);
        decision.ShouldStartCatchUpWindow.Should().BeFalse();
        decision.IsForcedCatchUpActive.Should().BeFalse();
        decision.QueueTarget.Should().Be(0);
    }

    [Fact]
    public void Evaluate_WhenPressureReturnsAfterCooldown_TriggersCatchUpAgain()
    {
        var policy = CreatePolicy(new StarvationGuardrailOptions
        {
            Enabled = true,
            MaxEligibleDeferAgeMinutes = 120,
            ForcedCatchUpQueueTargetFloor = 3,
            ForcedCatchUpCandidateBurstMultiplier = 1.5d,
            ForcedCatchUpMaxCandidateHardCap = 40
        });

        var firstActivation = policy.Evaluate(CreateInput(
            maxEligibleDeferAgeMinutes: 180,
            isCatchUpWindowActive: false,
            isCatchUpCooldownActive: false,
            baselineQueueTarget: 0,
            baselineMaxCandidates: 10));
        var duringCooldown = policy.Evaluate(CreateInput(
            maxEligibleDeferAgeMinutes: 200,
            isCatchUpWindowActive: false,
            isCatchUpCooldownActive: true,
            baselineQueueTarget: 0,
            baselineMaxCandidates: 10));
        var secondActivation = policy.Evaluate(CreateInput(
            maxEligibleDeferAgeMinutes: 210,
            isCatchUpWindowActive: false,
            isCatchUpCooldownActive: false,
            baselineQueueTarget: 0,
            baselineMaxCandidates: 10));

        firstActivation.Outcome.Should().Be(StarvationGuardrailOutcome.CatchUpWindowStart);
        duringCooldown.Outcome.Should().Be(StarvationGuardrailOutcome.CatchUpCooldown);
        secondActivation.Outcome.Should().Be(StarvationGuardrailOutcome.CatchUpWindowStart);
        secondActivation.QueueTarget.Should().Be(3);
    }

    private static StarvationGuardrailPolicy CreatePolicy(StarvationGuardrailOptions options) =>
        new(Options.Create(options));

    private static StarvationGuardrailInput CreateInput(
        double? maxEligibleDeferAgeMinutes,
        bool isCatchUpWindowActive,
        bool isCatchUpCooldownActive,
        int baselineQueueTarget,
        int baselineMaxCandidates) =>
        new(
            ProducerKey: "maintenance",
            EvaluationUtc: new DateTime(2026, 3, 5, 19, 0, 0, DateTimeKind.Utc),
            MaxEligibleDeferAgeMinutes: maxEligibleDeferAgeMinutes,
            IsCatchUpWindowActive: isCatchUpWindowActive,
            IsCatchUpCooldownActive: isCatchUpCooldownActive,
            BaselineQueueTarget: baselineQueueTarget,
            BaselineMaxCandidates: baselineMaxCandidates);
}
