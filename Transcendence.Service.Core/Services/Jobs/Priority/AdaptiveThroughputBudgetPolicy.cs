using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using Transcendence.Service.Core.Services.Jobs.Configuration;

namespace Transcendence.Service.Core.Services.Jobs.Priority;

public class AdaptiveThroughputBudgetPolicy(IOptions<AdaptiveThroughputBudgetOptions> options)
    : IAdaptiveThroughputBudgetPolicy
{
    private readonly ConcurrentDictionary<string, ModeState> _modeStates = new(StringComparer.Ordinal);

    public int VelocityLookbackMinutes => Math.Max(5, options.Value.VelocityLookbackMinutes);

    public AdaptiveThroughputBudgetDecision ComputeBudget(AdaptiveThroughputBudgetInput input)
    {
        var config = options.Value;
        var evaluationUtc = EnsureUtc(input.EvaluationUtc);
        var targetMatchesForPatch = Math.Max(1, input.TargetSuccessfulMatchesForPatch);
        var successfulMatchesForPatch = Math.Max(0, input.SuccessfulMatchesForPatch);
        var baselineMaxCandidates = Math.Max(1, input.BaselineMaxCandidates);
        var baselineMaxQueueTarget = Math.Max(1, input.BaselineMaxQueueTarget);
        var baselineMinQueueTarget = Math.Clamp(input.BaselineMinQueueTarget, 1, baselineMaxQueueTarget);

        var coverageRatio = Math.Clamp((double)successfulMatchesForPatch / targetMatchesForPatch, 0d, 2d);
        var includeAllModes = successfulMatchesForPatch >= targetMatchesForPatch;
        var backlogAgeMinutes = ResolveBacklogAgeMinutes(input.LatestPatchSuccessFetchUtc, evaluationUtc);
        var velocityPerHour = ResolveVelocityPerHour(input.RecentSuccessfulMatches, config.VelocityLookbackMinutes);
        var candidatePressureRatio = (double)Math.Max(0, input.PendingCandidateCount) / baselineMaxCandidates;

        var desiredMode = DetermineDesiredMode(
            input.IsApiPriorityDemandActive,
            coverageRatio,
            backlogAgeMinutes,
            velocityPerHour,
            candidatePressureRatio,
            config);
        var mode = ApplyHysteresis(input.ProducerKey, desiredMode, input.IsApiPriorityDemandActive, evaluationUtc, config);

        var queueTarget = ResolveQueueTarget(
            mode,
            coverageRatio,
            backlogAgeMinutes,
            candidatePressureRatio,
            baselineMinQueueTarget,
            baselineMaxQueueTarget,
            config);
        var maxCandidates = ResolveMaxCandidates(mode, baselineMaxCandidates, queueTarget, config);

        return new AdaptiveThroughputBudgetDecision(
            mode,
            maxCandidates,
            queueTarget,
            includeAllModes,
            coverageRatio,
            backlogAgeMinutes,
            velocityPerHour,
            candidatePressureRatio);
    }

    private static AdaptiveThroughputBudgetMode DetermineDesiredMode(
        bool isApiPriorityDemandActive,
        double coverageRatio,
        double backlogAgeMinutes,
        double velocityPerHour,
        double candidatePressureRatio,
        AdaptiveThroughputBudgetOptions options)
    {
        if (isApiPriorityDemandActive)
            return AdaptiveThroughputBudgetMode.HighPressure;

        if (coverageRatio < Math.Clamp(options.CatchUpCoverageThreshold, 0d, 1d))
            return AdaptiveThroughputBudgetMode.CatchUp;

        if (backlogAgeMinutes >= Math.Max(1, options.CatchUpBacklogAgeMinutes))
            return AdaptiveThroughputBudgetMode.CatchUp;

        if (candidatePressureRatio >= Math.Max(0.1d, options.CatchUpCandidatePressureThreshold) &&
            velocityPerHour < Math.Max(0.1d, options.MinimumRecentVelocityPerHour))
            return AdaptiveThroughputBudgetMode.CatchUp;

        return AdaptiveThroughputBudgetMode.Balanced;
    }

    private AdaptiveThroughputBudgetMode ApplyHysteresis(
        string producerKey,
        AdaptiveThroughputBudgetMode desiredMode,
        bool isApiPriorityDemandActive,
        DateTime evaluationUtc,
        AdaptiveThroughputBudgetOptions options)
    {
        var key = string.IsNullOrWhiteSpace(producerKey) ? "default" : producerKey.Trim();
        var currentState = _modeStates.GetOrAdd(key, _ => new ModeState(AdaptiveThroughputBudgetMode.Balanced, evaluationUtc));

        if (currentState.Mode == desiredMode)
            return desiredMode;

        var elapsedMinutes = Math.Max(0d, (evaluationUtc - currentState.LastTransitionUtc).TotalMinutes);
        var modeSwitchCooldown = Math.Max(0, options.ModeSwitchCooldownMinutes);
        var highPressureCooldown = Math.Max(0, options.HighPressureCooldownMinutes);
        var catchUpHoldMinutes = Math.Max(0, options.CatchUpHoldMinutes);

        var resolvedMode = desiredMode;
        if (currentState.Mode == AdaptiveThroughputBudgetMode.HighPressure &&
            !isApiPriorityDemandActive &&
            elapsedMinutes < highPressureCooldown)
        {
            resolvedMode = AdaptiveThroughputBudgetMode.HighPressure;
        }
        else if (currentState.Mode == AdaptiveThroughputBudgetMode.CatchUp &&
                 desiredMode == AdaptiveThroughputBudgetMode.Balanced &&
                 elapsedMinutes < catchUpHoldMinutes)
        {
            resolvedMode = AdaptiveThroughputBudgetMode.CatchUp;
        }
        else if (currentState.Mode == AdaptiveThroughputBudgetMode.Balanced &&
                 desiredMode == AdaptiveThroughputBudgetMode.CatchUp &&
                 elapsedMinutes < modeSwitchCooldown)
        {
            resolvedMode = AdaptiveThroughputBudgetMode.Balanced;
        }

        if (resolvedMode != currentState.Mode)
            _modeStates[key] = new ModeState(resolvedMode, evaluationUtc);

        return resolvedMode;
    }

    private static int ResolveQueueTarget(
        AdaptiveThroughputBudgetMode mode,
        double coverageRatio,
        double backlogAgeMinutes,
        double candidatePressureRatio,
        int baselineMinQueueTarget,
        int baselineMaxQueueTarget,
        AdaptiveThroughputBudgetOptions options)
    {
        if (mode == AdaptiveThroughputBudgetMode.HighPressure)
            return 0;

        if (mode == AdaptiveThroughputBudgetMode.CatchUp)
        {
            var catchUpMax = Math.Max(
                baselineMaxQueueTarget,
                (int)Math.Ceiling(baselineMaxQueueTarget * Math.Max(1d, options.CatchUpQueueBurstMultiplier)));
            var boundedCatchUpMax = Math.Min(Math.Max(1, options.MaxQueueTargetHardCap), catchUpMax);
            return Math.Clamp(boundedCatchUpMax, baselineMinQueueTarget, boundedCatchUpMax);
        }

        var coverageDeficitRatio = Math.Clamp(1d - Math.Min(1d, coverageRatio), 0d, 1d);
        var pressureSignal = Math.Clamp(candidatePressureRatio - 1d, 0d, 1d);
        var urgency = Math.Clamp((coverageDeficitRatio * 0.75d) + (pressureSignal * 0.25d), 0d, 1d);

        if (backlogAgeMinutes >= Math.Max(1, options.CatchUpBacklogAgeMinutes))
            return baselineMaxQueueTarget;

        var scaled = baselineMinQueueTarget +
                     (int)Math.Ceiling((baselineMaxQueueTarget - baselineMinQueueTarget) * urgency);
        return Math.Clamp(scaled, baselineMinQueueTarget, baselineMaxQueueTarget);
    }

    private static int ResolveMaxCandidates(
        AdaptiveThroughputBudgetMode mode,
        int baselineMaxCandidates,
        int queueTarget,
        AdaptiveThroughputBudgetOptions options)
    {
        var maxHardCap = Math.Max(1, options.MaxCandidateHardCap);

        if (mode == AdaptiveThroughputBudgetMode.HighPressure)
        {
            var reduced = (int)Math.Ceiling(baselineMaxCandidates * Math.Clamp(options.HighPressureCandidateMultiplier, 0d, 1d));
            return Math.Clamp(Math.Max(1, reduced), 1, maxHardCap);
        }

        if (mode == AdaptiveThroughputBudgetMode.CatchUp)
        {
            var boosted = (int)Math.Ceiling(baselineMaxCandidates * Math.Max(1d, options.CatchUpCandidateBurstMultiplier));
            return Math.Clamp(Math.Max(queueTarget, boosted), 1, maxHardCap);
        }

        return Math.Clamp(Math.Max(queueTarget, baselineMaxCandidates), 1, maxHardCap);
    }

    private static double ResolveVelocityPerHour(int recentSuccessfulMatches, int lookbackMinutes)
    {
        var boundedLookbackMinutes = Math.Max(5, lookbackMinutes);
        var lookbackHours = boundedLookbackMinutes / 60d;
        return Math.Max(0, recentSuccessfulMatches) / lookbackHours;
    }

    private static double ResolveBacklogAgeMinutes(DateTime? latestPatchSuccessFetchUtc, DateTime evaluationUtc)
    {
        if (!latestPatchSuccessFetchUtc.HasValue)
            return double.MaxValue;

        var latestUtc = EnsureUtc(latestPatchSuccessFetchUtc.Value);
        return Math.Max(0d, (evaluationUtc - latestUtc).TotalMinutes);
    }

    private static DateTime EnsureUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private sealed record ModeState(AdaptiveThroughputBudgetMode Mode, DateTime LastTransitionUtc);
}
