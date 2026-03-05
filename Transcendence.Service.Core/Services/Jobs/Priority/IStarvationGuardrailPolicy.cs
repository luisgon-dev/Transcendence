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
    StarvationGuardrailDecision Evaluate(StarvationGuardrailInput input);
}
