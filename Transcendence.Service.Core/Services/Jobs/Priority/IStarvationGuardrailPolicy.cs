namespace Transcendence.Service.Core.Services.Jobs.Priority;

public readonly record struct StarvationGuardrailInput(
    string ProducerKey,
    DateTime EvaluationUtc,
    double? MaxEligibleDeferAgeMinutes,
    bool IsCatchUpWindowActive,
    bool IsCatchUpCooldownActive,
    int BaselineQueueTarget,
    int BaselineMaxCandidates);

public readonly record struct StarvationGuardrailDecision(
    StarvationGuardrailOutcome Outcome,
    bool IsForcedCatchUpActive,
    bool ShouldStartCatchUpWindow,
    int QueueTarget,
    int MaxCandidates,
    double MaxEligibleDeferAgeMinutes,
    double DeferAgeThresholdMinutes,
    TimeSpan CatchUpWindowTtl,
    TimeSpan CatchUpCooldownTtl);

public enum StarvationGuardrailOutcome
{
    Disabled,
    Normal,
    CatchUpWindowStart,
    CatchUpWindowContinue,
    CatchUpCooldown
}

public interface IStarvationGuardrailPolicy
{
    /// <summary>Whether the guardrail is enabled. When false, callers can skip computing its
    /// (expensive) inputs since Evaluate returns Disabled regardless of them.</summary>
    bool Enabled { get; }

    StarvationGuardrailDecision Evaluate(StarvationGuardrailInput input);
}
