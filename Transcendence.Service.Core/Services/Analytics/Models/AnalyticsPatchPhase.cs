namespace Transcendence.Service.Core.Services.Analytics.Models;

public enum AnalyticsPatchPhase
{
    Bootstrap,
    Provisional,
    Maturing,
    Steady
}

public static class AnalyticsPatchPhaseCalculator
{
    public static AnalyticsPatchPhase Resolve(double patchAgeHours, ChampionAnalyticsComputeOptions options)
    {
        var ageHours = Math.Max(0, patchAgeHours);
        var bootstrapWindowHours = Math.Max(1, options.BootstrapWindowHours);
        var provisionalWindowHours = Math.Max(bootstrapWindowHours, options.ProvisionalWindowHours);
        var maturingWindowHours = Math.Max(provisionalWindowHours, options.MaturingWindowHours);

        if (ageHours < bootstrapWindowHours)
            return AnalyticsPatchPhase.Bootstrap;
        if (ageHours < provisionalWindowHours)
            return AnalyticsPatchPhase.Provisional;
        if (ageHours < maturingWindowHours)
            return AnalyticsPatchPhase.Maturing;

        return AnalyticsPatchPhase.Steady;
    }

    public static int RecommendedSampleSize(AnalyticsPatchPhase phase, ChampionAnalyticsComputeOptions options) =>
        phase switch
        {
            AnalyticsPatchPhase.Bootstrap => Math.Max(1, options.BootstrapPatchMinimumGamesRequired),
            AnalyticsPatchPhase.Provisional => Math.Max(1, options.EarlyPatchMinimumGamesRequired),
            AnalyticsPatchPhase.Maturing => Math.Max(1, options.MaturingPatchMinimumGamesRequired),
            _ => Math.Max(1, options.MinimumGamesRequired)
        };
}
