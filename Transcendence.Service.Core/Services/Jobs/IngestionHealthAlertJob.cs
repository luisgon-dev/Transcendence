using Hangfire;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Transcendence.Service.Core.Services.Diagnostics;

namespace Transcendence.Service.Core.Services.Jobs;

/// <summary>
/// Polls Hangfire health every few minutes and pushes an alert (webhook, or log-only when no webhook
/// URL is configured) on a degradation signal: a failed-job spike, a stuck discovery backlog, or zero
/// throughput while work is enqueued (the stall class the discovery lane was hardened against). A
/// per-condition cooldown stops a persistent condition from re-alerting every run. Poll-based, so it
/// needs no metrics exporter and can ship ahead of full telemetry (P3.1).
/// </summary>
public sealed class IngestionHealthAlertJob(
    JobStorage jobStorage,
    IQueueDepthProbe queueDepthProbe,
    IAlertNotifier notifier,
    IDistributedCache cache,
    IOptions<AlertOptions> options,
    ILogger<IngestionHealthAlertJob> logger)
{
    private const string PrevSucceededKey = "alert:prevSucceeded";
    private const string CooldownKeyPrefix = "alert:cooldown:";

    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        var opts = options.Value;
        var stats = jobStorage.GetMonitoringApi().GetStatistics();
        var discoveryDepth = queueDepthProbe.GetEnqueuedCount(HangfireQueues.Discovery);

        long? prevSucceeded = null;
        var prevRaw = await cache.GetStringAsync(PrevSucceededKey, ct).ConfigureAwait(false);
        if (long.TryParse(prevRaw, out var parsed))
        {
            prevSucceeded = parsed;
        }
        await cache.SetStringAsync(PrevSucceededKey, stats.Succeeded.ToString(), ct).ConfigureAwait(false);

        var conditions = EvaluateConditions(
            stats.Failed, stats.Succeeded, prevSucceeded, stats.Enqueued, discoveryDepth, opts);

        foreach (var (key, message) in conditions)
        {
            // Per-condition cooldown so a persistent condition does not re-alert on every poll.
            var cooldownKey = CooldownKeyPrefix + key;
            if (await cache.GetStringAsync(cooldownKey, ct).ConfigureAwait(false) is not null)
            {
                continue;
            }

            logger.LogWarning("Ingestion health alert [{Key}]: {Message}", key, message);
            await notifier.SendAsync($"Transcendence ingestion: {key}", message, ct).ConfigureAwait(false);
            await cache.SetStringAsync(
                cooldownKey,
                "1",
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = opts.Cooldown },
                ct).ConfigureAwait(false);
        }
    }

    /// <summary>Pure condition evaluation, separated for testability.</summary>
    public static IReadOnlyList<(string Key, string Message)> EvaluateConditions(
        long failed,
        long succeeded,
        long? prevSucceeded,
        long enqueued,
        long discoveryDepth,
        AlertOptions opts)
    {
        var result = new List<(string, string)>();

        if (failed > opts.FailedJobThreshold)
        {
            result.Add(("failed-jobs",
                $"{failed} failed Hangfire jobs (threshold {opts.FailedJobThreshold})."));
        }

        if (discoveryDepth > opts.DiscoveryQueueDepthThreshold)
        {
            result.Add(("discovery-backlog",
                $"Discovery queue depth {discoveryDepth} exceeds {opts.DiscoveryQueueDepthThreshold} — workers may be stuck."));
        }

        // Throughput stall: nothing completed since the last check while work is waiting.
        if (prevSucceeded.HasValue && succeeded == prevSucceeded.Value && (enqueued + discoveryDepth) > 0)
        {
            result.Add(("throughput-stall",
                $"No Hangfire jobs completed since the last check while {enqueued + discoveryDepth} are enqueued — ingestion appears stalled."));
        }

        return result;
    }
}
