using System.Diagnostics;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Transcendence.Data;
using Transcendence.Service.Core.Services.Jobs.Configuration;
using Transcendence.Service.Core.Services.Leaderboards.Implementations;
using Transcendence.Service.Core.Services.Leaderboards.Interfaces;

namespace Transcendence.Service.Core.Services.Jobs;

[DisableConcurrentExecution(600)]
[AutomaticRetry(Attempts = 0)]
public sealed class RefreshLeaderboardsJob(
    TranscendenceContext db,
    ILeaderboardService service,
    IOptions<MultiRegionIngestionOptions> regions,
    ILogger<RefreshLeaderboardsJob> logger)
{
    [Queue(HangfireQueues.AnalyticsWarm)]
    public async Task ExecuteAsync(CancellationToken ct)
    {
        var updated = await db.AnalyticsResponseSnapshots.AsNoTracking()
            .Where(row => row.Feature == LeaderboardService.SnapshotFeature && row.Patch == "*")
            .ToDictionaryAsync(row => row.ScopeKey, row => row.ComputedAtUtc, ct);
        var platforms = regions.Value.Regions.Where(region => region.Enabled).Select(region => region.Region)
            .Append("NA1").Distinct(StringComparer.OrdinalIgnoreCase);
        var boards = platforms.SelectMany(platform => new[] { "solo", "flex" }
                .Select(queue => new { Platform = platform.ToUpperInvariant(), Queue = queue }))
            .OrderBy(board => updated.GetValueOrDefault(
                $"{board.Platform}:{LeaderboardService.NormalizeQueue(board.Queue)}", DateTime.MinValue))
            .Take(8).ToList();
        var started = Stopwatch.StartNew();
        foreach (var board in boards)
        {
            if (started.Elapsed.TotalSeconds >= 120)
                break;
            try
            {
                await service.RefreshRegionalAsync(board.Platform, board.Queue, ct);
                await Task.Delay(500, ct);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                db.ChangeTracker.Clear();
                logger.LogWarning(exception, "Failed to refresh {Platform} {Queue} leaderboard; previous snapshot retained.",
                    board.Platform, board.Queue);
            }
        }
    }
}
