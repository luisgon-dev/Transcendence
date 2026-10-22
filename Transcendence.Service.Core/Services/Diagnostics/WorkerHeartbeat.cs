using System.Globalization;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Transcendence.Service.Core.Services.Diagnostics;

/// <summary>
/// Records a liveness "beat" from Hangfire job progress so a watchdog can tell whether the
/// worker is still executing Hangfire jobs (vs. the documented hang where every worker parks on
/// a non-stoppable job and the process sits idle). Distinct from the dispatcher pacing cron —
/// this is the process-liveness marker.
/// </summary>
public interface IWorkerHeartbeat
{
    /// <summary>Record actual job progress without performing I/O on a Hangfire worker thread.</summary>
    void RecordProgress();

    /// <summary>Publish the most recent progress timestamp without advancing it.</summary>
    Task PublishAsync(CancellationToken cancellationToken = default);

    /// <summary>UTC time of the last recorded job progress, or null if none yet.</summary>
    DateTime? LastBeatUtc { get; }

    /// <summary>
    /// Record progress: updates the in-memory marker (read by the watchdog), the heartbeat
    /// file (read by the container HEALTHCHECK), and a Redis key (external observability / alerting).
    /// File and Redis writes are best-effort and never throw into the caller.
    /// </summary>
    Task BeatAsync(CancellationToken cancellationToken = default);
}

public sealed class WorkerHeartbeat(
    IDistributedCache cache,
    IOptions<WorkerWatchdogOptions> options,
    ILogger<WorkerHeartbeat> logger) : IWorkerHeartbeat
{
    /// <summary>Redis key other processes (WebAPI admin, alerting) can read for worker liveness.</summary>
    public const string RedisKey = "worker:heartbeat:lastBeatUtc";

    private long _lastBeatTicks; // 0 = never beat

    public DateTime? LastBeatUtc
    {
        get
        {
            var ticks = Interlocked.Read(ref _lastBeatTicks);
            return ticks == 0 ? null : new DateTime(ticks, DateTimeKind.Utc);
        }
    }

    public void RecordProgress() => Interlocked.Exchange(ref _lastBeatTicks, DateTime.UtcNow.Ticks);

    public async Task BeatAsync(CancellationToken cancellationToken = default)
    {
        RecordProgress();
        await PublishAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task PublishAsync(CancellationToken cancellationToken = default)
    {
        if (LastBeatUtc is not { } now)
            return;

        var stamp = now.ToString("O", CultureInfo.InvariantCulture);

        // File marker for the container HEALTHCHECK.
        var path = options.Value.HeartbeatFilePath;
        if (!string.IsNullOrWhiteSpace(path))
        {
            try
            {
                await File.WriteAllTextAsync(path, stamp, cancellationToken).ConfigureAwait(false);
                // HEALTHCHECK reads mtime. Publishing an old beat must never make a stalled
                // worker look healthy just because this publisher is still scheduled.
                File.SetLastWriteTimeUtc(path, now);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Worker heartbeat: failed to write heartbeat file {Path}", path);
            }
        }

        // Redis marker for external observability / alerting.
        try
        {
            await cache.SetStringAsync(
                RedisKey,
                stamp,
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1) },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Worker heartbeat: failed to write Redis heartbeat key");
        }
    }
}
