using Microsoft.EntityFrameworkCore;
using Transcendence.Data;
using Transcendence.Service.Core.Services.Analytics.Models;

namespace Transcendence.Service.Core.Services.Analytics.Implementations;

/// <summary>
/// Shared adaptive sample-size threshold for champion analytics: the minimum games a (role, tier)
/// must have before its win rate is trusted, scaled down during the new-patch ramp so early-patch
/// pages still render. Extracted from the original analytics compute service (P10.1) so the win-rate /
/// tier-list path (<see cref="ChampionWinRateComputeService"/>) and the builds path share one rule
/// instead of duplicating it (a divergence would silently make the two surfaces disagree on what
/// counts as enough data). Static + all inputs passed in, so it adds no new injected dependency.
/// </summary>
internal static class AnalyticsSampleThreshold
{
    public static async Task<int> ResolveAsync(
        TranscendenceContext context,
        ChampionAnalyticsComputeOptions options,
        string patch,
        CancellationToken ct)
    {
        var steadyStateMinimum = Math.Max(1, options.MinimumGamesRequired);

        var releaseDate = await context.Patches
            .AsNoTracking()
            .Where(p => p.Version == patch)
            .Select(p => (DateTime?)p.ReleaseDate)
            .FirstOrDefaultAsync(ct);

        if (!releaseDate.HasValue)
            return steadyStateMinimum;

        var releaseUtc = releaseDate.Value.Kind == DateTimeKind.Utc
            ? releaseDate.Value
            : DateTime.SpecifyKind(releaseDate.Value, DateTimeKind.Utc);
        var patchAgeHours = Math.Max(0, (DateTime.UtcNow - releaseUtc).TotalHours);
        var patchPhase = AnalyticsPatchPhaseCalculator.Resolve(patchAgeHours, options);
        return AnalyticsPatchPhaseCalculator.RecommendedSampleSize(patchPhase, options);
    }
}
