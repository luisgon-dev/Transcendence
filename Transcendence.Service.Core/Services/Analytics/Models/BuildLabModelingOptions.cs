namespace Transcendence.Service.Core.Services.Analytics.Models;

public sealed class BuildLabModelingOptions
{
    public bool Enabled { get; set; } = false;
    public string DatasetVersion { get; set; } = "build-lab-v1";
    public string CodeRevision { get; set; } = "local";
    public int PriorPatchesToBorrow { get; set; } = 2;
    public int MinimumObservedActions { get; set; } = 1000;
    public int MinimumEffectiveSampleSize { get; set; } = 500;
    public double MaximumConfidenceIntervalWidth { get; set; } = 0.03;
    public double MinimumPropensityOverlap { get; set; } = 0.90;
    public double MaximumCovariateBalance { get; set; } = 0.10;
    public double MaximumOverallEce { get; set; } = 0.015;
    public double MaximumTimeBandEce { get; set; } = 0.025;
    public int RetainedGenerations { get; set; } = 4;
    public int RetiredGenerationGraceMinutes { get; set; } = 30;
}

// Every gate field is nullable so a metric the modeler never reported fails closed instead of
// binding to a default that happens to pass.
public sealed record BuildLabValidationMetrics(
    double? OverallEce,
    double? MaxTimeBandEce,
    double? BrierScore,
    double? BaselineBrierScore,
    double? LogLoss,
    double? BaselineLogLoss,
    bool? HeldOutPatchPassed,
    bool? LeakageCheckPassed,
    // Whether a held-out-patch test was possible at all. A cohort covering one patch cannot be split
    // across a patch boundary, so HeldOutPatchPassed is false there for want of a test rather than
    // because a test failed. Null means the modeler predates the field, which is read as "applicable"
    // so an old manifest still has to clear the gate.
    bool? HeldOutPatchApplicable = null,
    // Whether any game phase is worse calibrated than a perfectly calibrated model would look at this
    // sample size and dependence structure. ECE is positively biased, so a fixed limit is unpassable on
    // thin data and toothless on plentiful data; the modeler measures the floor by resampling whole
    // teams and reports whether the deviation is DETECTABLE. Null means an older modeler that did not
    // measure it, in which case the raw limit still applies on its own.
    bool? CalibrationExceedsNoiseFloor = null,
    // How far the worst phase sits ABOVE its own noise floor. The practical half of the gate: a
    // deviation too small to matter should not block a publish even when it is detectable, and one
    // large enough to matter should block even when the sample is too thin to call it significant.
    double? MaxTimeBandEceExcess = null);
