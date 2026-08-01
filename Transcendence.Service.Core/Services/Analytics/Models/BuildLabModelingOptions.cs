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
    bool? LeakageCheckPassed);
