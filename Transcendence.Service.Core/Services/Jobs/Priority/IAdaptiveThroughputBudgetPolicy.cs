namespace Transcendence.Service.Core.Services.Jobs.Priority;

public enum AdaptiveThroughputBudgetMode
{
    HighPressure,
    Balanced,
    CatchUp
}

public readonly record struct AdaptiveThroughputBudgetInput(
    string ProducerKey,
    DateTime EvaluationUtc,
    bool IsApiPriorityDemandActive,
    int SuccessfulMatchesForPatch,
    int TargetSuccessfulMatchesForPatch,
    DateTime? LatestPatchSuccessFetchUtc,
    int RecentSuccessfulMatches,
    int PendingCandidateCount,
    int BaselineMaxCandidates,
    int BaselineMinQueueTarget,
    int BaselineMaxQueueTarget);

public readonly record struct AdaptiveThroughputBudgetDecision(
    AdaptiveThroughputBudgetMode Mode,
    int MaxCandidates,
    int QueueTarget,
    bool IncludeAllModes,
    double CoverageRatio,
    double BacklogAgeMinutes,
    double RecentVelocityPerHour,
    double CandidatePressureRatio);

public interface IAdaptiveThroughputBudgetPolicy
{
    int VelocityLookbackMinutes { get; }

    AdaptiveThroughputBudgetDecision ComputeBudget(AdaptiveThroughputBudgetInput input);
}
