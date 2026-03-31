namespace Transcendence.Service.Core.Services.Analytics.Models;

public enum AnalyticsSampleStatus
{
    Sufficient,
    LowSample,
    NoData
}

public record AnalyticsSampleMetadata(
    AnalyticsSampleStatus SampleStatus,
    int SampleSize,
    int MinimumRecommendedSampleSize,
    double PatchAgeHours,
    bool IsEarlyPatchWindow,
    AnalyticsPatchPhase PatchPhase,
    bool IsProvisional
);
