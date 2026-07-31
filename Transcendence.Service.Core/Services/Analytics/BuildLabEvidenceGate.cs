using System.Linq.Expressions;
using Transcendence.Data.Models.LoL.Analytics;
using Transcendence.Service.Core.Services.Analytics.Models;

namespace Transcendence.Service.Core.Services.Analytics;

/// <summary>
/// Version-one publication policy. Keeping the decision pure makes threshold changes and shadow
/// validation independently testable from EF promotion mechanics.
/// </summary>
public static class BuildLabEvidenceGate
{
    public const int V1MinimumObservedActions = 1000;
    public const int V1MinimumEffectiveSampleSize = 500;
    public const double V1MaximumConfidenceIntervalWidth = 0.03;
    public const double V1MinimumPropensityOverlap = 0.90;
    public const double V1MaximumCovariateBalance = 0.10;
    public const double V1MaximumOverallEce = 0.015;
    public const double V1MaximumTimeBandEce = 0.025;

    /// <summary>
    /// Lift, in win-probability points, that separates "typical" from "above/below average". Wide
    /// enough that a bucket claim is about a real difference rather than estimator noise.
    /// </summary>
    public const double BucketThreshold = 0.005;

    /// <summary>
    /// Posterior mass a single bucket must hold before the bucket itself is publishable. A cell that
    /// straddles two buckets says nothing, so it stays descriptive.
    /// </summary>
    public const double BucketConfidence = 0.80;

    public readonly record struct PublicationGate(
        string UnavailableReason,
        Expression<Func<AdjustedActionEstimate, bool>> Fails);

    /// <summary>
    /// Rows that cannot support a number but can still support a direction.
    /// </summary>
    /// <remarks>
    /// Publication is deliberately not all-or-nothing: patches ship fortnightly, and a cell needs far
    /// more evidence for a &lt;=3pp interval than for a direction, so gating everything on the interval
    /// leaves the lab empty for most of a patch.
    ///
    /// Every sample gate still applies — bucketing thin evidence is no more honest than publishing a
    /// number for it. Only the interval-width gate is traded away, and only when the posterior
    /// actually concentrates in one bucket. <c>BucketConfidence</c> is the posterior mass the modeler
    /// measured for the favoured bucket, so the decision stays set-based over hundreds of thousands
    /// of rows instead of materialising them to evaluate a normal tail in memory.
    /// </remarks>
    public static Expression<Func<AdjustedActionEstimate, bool>> QualifiesForBucketedTier(
        BuildLabModelingOptions options) =>
        estimate =>
            estimate.ObservedCount >= options.MinimumObservedActions &&
            estimate.EffectiveSampleSize >= options.MinimumEffectiveSampleSize &&
            estimate.PropensityOverlap >= options.MinimumPropensityOverlap &&
            estimate.CovariateBalance <= options.MaximumCovariateBalance &&
            estimate.StableAcrossFolds &&
            estimate.AdjustedWpa != null &&
            estimate.BucketConfidence >= BucketConfidence;

    /// <summary>
    /// Ordered most significant first. Promotion grades hundreds of thousands of rows through these
    /// expressions server-side, so the policy has exactly one definition.
    /// </summary>
    public static IReadOnlyList<PublicationGate> ActionGates(BuildLabModelingOptions options) =>
    [
        new($"Needs at least {options.MinimumObservedActions:N0} observed actions.",
            estimate => estimate.ObservedCount < options.MinimumObservedActions),
        new($"Effective sample size is below {options.MinimumEffectiveSampleSize:N0}.",
            estimate => estimate.EffectiveSampleSize < options.MinimumEffectiveSampleSize),
        new("The confidence interval is too wide.",
            estimate => estimate.ConfidenceLow == null ||
                        estimate.ConfidenceHigh == null ||
                        estimate.ConfidenceHigh.Value - estimate.ConfidenceLow.Value >
                            options.MaximumConfidenceIntervalWidth),
        new("Comparable alternative choices do not have enough overlap.",
            estimate => estimate.PropensityOverlap < options.MinimumPropensityOverlap),
        new("Weighted contexts remain imbalanced.",
            estimate => estimate.CovariateBalance > options.MaximumCovariateBalance),
        new("The estimate is not stable across chronological folds.",
            estimate => !estimate.StableAcrossFolds),
        new("The adjusted estimate is unavailable.",
            estimate => estimate.AdjustedWpa == null)
    ];

    /// <summary>
    /// Applies every gate as a passing filter. Grading publishes through this rather than by falsifying
    /// a blanket-true pass, so a partially graded generation is never more permissive than its final
    /// state.
    /// </summary>
    public static IQueryable<AdjustedActionEstimate> WherePassesEveryGate(
        IQueryable<AdjustedActionEstimate> estimates,
        BuildLabModelingOptions options)
    {
        foreach (var gate in ActionGates(options))
            estimates = estimates.Where(Not(gate.Fails));
        return estimates;
    }

    /// <summary>
    /// Regional cells a publishable pooled global twin already covers: the same scope key, and a
    /// difference the family-wise corrected interval cannot separate. A cell with no publishable twin is
    /// never mirrored — there would be nothing to fall back to — so it publishes on its own evidence.
    /// </summary>
    public static IQueryable<AdjustedActionEstimate> WhereMirroredByPublishableGlobal(
        IQueryable<AdjustedActionEstimate> regionalEstimates,
        IQueryable<AdjustedActionEstimate> allEstimates,
        double correctedCritical) =>
        regionalEstimates.Where(MirroredByPublishableGlobal(allEstimates, correctedCritical));

    public static IQueryable<AdjustedActionEstimate> WhereNotMirroredByPublishableGlobal(
        IQueryable<AdjustedActionEstimate> regionalEstimates,
        IQueryable<AdjustedActionEstimate> allEstimates,
        double correctedCritical) =>
        regionalEstimates.Where(Not(MirroredByPublishableGlobal(allEstimates, correctedCritical)));

    /// <summary>
    /// A conservative family-wise correction. It grows with the number of regional cells evaluated in the
    /// generation and therefore never makes publication easier.
    /// </summary>
    public static double CorrectedCriticalValue(int comparisonCount) =>
        1.96 + Math.Sqrt(2 * Math.Log(Math.Max(1, comparisonCount)));

    public static Expression<Func<AdjustedPathEstimate, bool>> PathGateFails(
        BuildLabModelingOptions options) =>
        path => path.AdjustedLift == null ||
                path.ObservedCount < options.MinimumObservedActions ||
                path.EffectiveSampleSize < options.MinimumEffectiveSampleSize ||
                path.ConfidenceLow == null ||
                path.ConfidenceHigh == null ||
                path.ConfidenceHigh.Value - path.ConfidenceLow.Value >
                    options.MaximumConfidenceIntervalWidth;

    public static Expression<Func<AdjustedPathEstimate, bool>> PathGatePasses(
        BuildLabModelingOptions options) => Not(PathGateFails(options));

    public static bool Evaluate(
        AdjustedActionEstimate estimate,
        BuildLabModelingOptions options,
        out string? unavailableReason)
    {
        foreach (var gate in ActionGates(options))
        {
            if (gate.Fails.Compile().Invoke(estimate))
            {
                unavailableReason = gate.UnavailableReason;
                return false;
            }
        }

        unavailableReason = null;
        return true;
    }

    /// <summary>
    /// The v1 floor every generation must clear, covering the per-estimate evidence gates and the
    /// model calibration ceilings alike: a relaxed ECE limit publishes miscalibrated probabilities just
    /// as surely as a relaxed sample floor. It is deliberately not keyed on DatasetVersion: that string
    /// is operator config bound from the same section as the thresholds, so a methodology that needs
    /// different floors has to add a constant here and change code.
    /// </summary>
    public static bool UsesV1OrStricterThresholds(BuildLabModelingOptions options) =>
        options.MinimumObservedActions >= V1MinimumObservedActions &&
        options.MinimumEffectiveSampleSize >= V1MinimumEffectiveSampleSize &&
        options.MaximumConfidenceIntervalWidth <= V1MaximumConfidenceIntervalWidth &&
        options.MinimumPropensityOverlap >= V1MinimumPropensityOverlap &&
        options.MaximumCovariateBalance <= V1MaximumCovariateBalance &&
        options.MaximumOverallEce <= V1MaximumOverallEce &&
        options.MaximumTimeBandEce <= V1MaximumTimeBandEce;

    public static bool RegionalOverrideIsMeaningful(
        AdjustedActionEstimate regional,
        AdjustedActionEstimate global,
        int comparisonCount)
    {
        if (!regional.AdjustedWpa.HasValue ||
            !global.AdjustedWpa.HasValue ||
            !regional.ConfidenceLow.HasValue ||
            !regional.ConfidenceHigh.HasValue ||
            !global.ConfidenceLow.HasValue ||
            !global.ConfidenceHigh.HasValue)
            return false;

        var regionalSe = (regional.ConfidenceHigh.Value - regional.ConfidenceLow.Value) / 3.92;
        var globalSe = (global.ConfidenceHigh.Value - global.ConfidenceLow.Value) / 3.92;
        var combinedSe = Math.Sqrt(regionalSe * regionalSe + globalSe * globalSe);
        return Math.Abs(regional.AdjustedWpa.Value - global.AdjustedWpa.Value) >
               CorrectedCriticalValue(comparisonCount) * combinedSe;
    }

    // The server-side twin of RegionalOverrideIsMeaningful, negated: a regional cell is mirrored when the
    // corrected interval cannot separate it from a pooled global estimate that is itself publishable.
    private static Expression<Func<AdjustedActionEstimate, bool>> MirroredByPublishableGlobal(
        IQueryable<AdjustedActionEstimate> allEstimates,
        double correctedCritical) =>
        regional => allEstimates.Any(pooled =>
            pooled.GenerationId == regional.GenerationId &&
            pooled.RegionScope == "GLOBAL" &&
            pooled.IsPublishable &&
            pooled.ChampionId == regional.ChampionId &&
            pooled.Role == regional.Role &&
            pooled.OpponentChampionId == regional.OpponentChampionId &&
            pooled.DecisionFamily == regional.DecisionFamily &&
            pooled.Stage == regional.Stage &&
            pooled.PathPrefixHash == regional.PathPrefixHash &&
            pooled.ActionKey == regional.ActionKey &&
            Math.Abs(regional.AdjustedWpa!.Value - pooled.AdjustedWpa!.Value) <=
                correctedCritical * Math.Sqrt(
                    (regional.ConfidenceHigh!.Value - regional.ConfidenceLow!.Value) *
                    (regional.ConfidenceHigh!.Value - regional.ConfidenceLow!.Value) / (3.92 * 3.92) +
                    (pooled.ConfidenceHigh!.Value - pooled.ConfidenceLow!.Value) *
                    (pooled.ConfidenceHigh!.Value - pooled.ConfidenceLow!.Value) / (3.92 * 3.92)));

    // Reuses the gate lambda's own parameter, so the negation stays one server-side predicate instead of
    // a second definition of the same policy that could drift from it.
    private static Expression<Func<T, bool>> Not<T>(Expression<Func<T, bool>> predicate) =>
        Expression.Lambda<Func<T, bool>>(Expression.Not(predicate.Body), predicate.Parameters);
}
