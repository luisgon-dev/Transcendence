using System.Diagnostics;
using Hangfire;
using Transcendence.Service.Core.Services.Analytics.Interfaces;

namespace Transcendence.Service.Core.Services.Jobs;

/// <summary>
/// Recomputes the public dataset stats (matches stored, ingest rate, platforms, players, patch) and
/// stores them for <c>GET /api/lol/analytics/dataset</c>. The exact <c>Matches</c> count lives here, on a
/// schedule, and never on the request path. It is a short aggregate, so it shares the analytics lane.
/// </summary>
[DisableConcurrentExecution(timeoutInSeconds: 10 * 60)]
public sealed class RefreshDatasetStatsJob(
    IDatasetStatsRefresher refresher,
    ILogger<RefreshDatasetStatsJob> logger)
{
    [Queue(HangfireQueues.AnalyticsWarm)]
    public async Task ExecuteAsync(CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        var stats = await refresher.RefreshAsync(DateTime.UtcNow, ct);

        logger.LogInformation(
            "Dataset stats refreshed in {ElapsedMs}ms: {Matches} matches, {Last24Hours} in the last 24h, {PerDay}/day over 7 days, {Platforms} platforms.",
            stopwatch.ElapsedMilliseconds,
            stats.MatchesStored,
            stats.MatchesLast24Hours,
            stats.MatchesPerDayLast7Days,
            stats.Platforms.Count);
    }
}
