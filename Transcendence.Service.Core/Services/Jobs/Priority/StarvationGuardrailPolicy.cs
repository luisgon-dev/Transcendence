using Microsoft.Extensions.Options;
using Transcendence.Service.Core.Services.Jobs.Configuration;

namespace Transcendence.Service.Core.Services.Jobs.Priority;

public class StarvationGuardrailPolicy(IOptions<StarvationGuardrailOptions> options) : IStarvationGuardrailPolicy
{
    public bool Enabled => options.Value.Enabled;

    public StarvationGuardrailDecision Evaluate(StarvationGuardrailInput input)
    {
        var config = options.Value;
        var catchUpWindowTtl = TimeSpan.FromMinutes(Math.Max(1, config.CatchUpWindowMinutes));
        var catchUpCooldownTtl = TimeSpan.FromMinutes(Math.Max(0, config.CatchUpCooldownMinutes));
        var baselineQueueTarget = Math.Max(0, input.BaselineQueueTarget);
        var baselineMaxCandidates = Math.Max(1, input.BaselineMaxCandidates);
        var deferAgeThresholdMinutes = Math.Max(1, config.MaxEligibleDeferAgeMinutes);
        var maxEligibleDeferAgeMinutes = Math.Max(0d, input.MaxEligibleDeferAgeMinutes ?? 0d);

        if (!config.Enabled)
        {
            return new StarvationGuardrailDecision(
                StarvationGuardrailOutcome.Disabled,
                IsForcedCatchUpActive: false,
                ShouldStartCatchUpWindow: false,
                QueueTarget: baselineQueueTarget,
                MaxCandidates: baselineMaxCandidates,
                MaxEligibleDeferAgeMinutes: maxEligibleDeferAgeMinutes,
                DeferAgeThresholdMinutes: deferAgeThresholdMinutes,
                CatchUpWindowTtl: catchUpWindowTtl,
                CatchUpCooldownTtl: catchUpCooldownTtl);
        }

        var deferAgeBreachDetected = input.MaxEligibleDeferAgeMinutes.HasValue &&
                                     input.MaxEligibleDeferAgeMinutes.Value >= deferAgeThresholdMinutes;
        var continueCatchUp = input.IsCatchUpWindowActive;
        var startCatchUp = !continueCatchUp && !input.IsCatchUpCooldownActive && deferAgeBreachDetected;
        var isForcedCatchUpActive = continueCatchUp || startCatchUp;

        var outcome = StarvationGuardrailOutcome.Normal;
        if (continueCatchUp)
            outcome = StarvationGuardrailOutcome.CatchUpWindowContinue;
        else if (startCatchUp)
            outcome = StarvationGuardrailOutcome.CatchUpWindowStart;
        else if (input.IsCatchUpCooldownActive)
            outcome = StarvationGuardrailOutcome.CatchUpCooldown;

        if (!isForcedCatchUpActive)
        {
            return new StarvationGuardrailDecision(
                outcome,
                IsForcedCatchUpActive: false,
                ShouldStartCatchUpWindow: false,
                QueueTarget: baselineQueueTarget,
                MaxCandidates: baselineMaxCandidates,
                MaxEligibleDeferAgeMinutes: maxEligibleDeferAgeMinutes,
                DeferAgeThresholdMinutes: deferAgeThresholdMinutes,
                CatchUpWindowTtl: catchUpWindowTtl,
                CatchUpCooldownTtl: catchUpCooldownTtl);
        }

        var forcedQueueFloor = Math.Max(1, config.ForcedCatchUpQueueTargetFloor);
        var forcedQueueTarget = Math.Max(baselineQueueTarget, forcedQueueFloor);

        var burstMultiplier = Math.Max(1d, config.ForcedCatchUpCandidateBurstMultiplier);
        var forcedCandidates = (int)Math.Ceiling(baselineMaxCandidates * burstMultiplier);
        var forcedHardCap = Math.Max(1, config.ForcedCatchUpMaxCandidateHardCap);
        var forcedMaxCandidates = Math.Clamp(
            Math.Max(forcedCandidates, forcedQueueTarget),
            1,
            forcedHardCap);

        return new StarvationGuardrailDecision(
            outcome,
            IsForcedCatchUpActive: true,
            ShouldStartCatchUpWindow: startCatchUp,
            QueueTarget: forcedQueueTarget,
            MaxCandidates: forcedMaxCandidates,
            MaxEligibleDeferAgeMinutes: maxEligibleDeferAgeMinutes,
            DeferAgeThresholdMinutes: deferAgeThresholdMinutes,
            CatchUpWindowTtl: catchUpWindowTtl,
            CatchUpCooldownTtl: catchUpCooldownTtl);
    }
}
