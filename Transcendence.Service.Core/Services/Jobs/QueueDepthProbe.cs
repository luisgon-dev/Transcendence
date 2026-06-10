using Hangfire;

namespace Transcendence.Service.Core.Services.Jobs;

/// <summary>
/// Reads the current enqueued depth of a Hangfire queue. Used by the discovery producers to apply
/// backpressure: when the discovery lane is already deep, the producers stop (or scale down) enqueuing
/// more refresh consumers so the queue cannot grow unbounded the way it did before the dedicated lane
/// existed (a ~100k stranded backlog). The workers — not producer output — are the bottleneck at that
/// point, so adding more is pure waste and risk.
/// </summary>
public interface IQueueDepthProbe
{
    long GetEnqueuedCount(string queue);
}

public static class QueueBackpressure
{
    /// <summary>
    /// Scales a per-run queue target down by current queue depth: unchanged at/below the soft cap,
    /// linearly toward zero between soft and hard, and zero at/above the hard cap.
    /// </summary>
    public static int Apply(int queueTarget, long depth, int softCap, int hardCap)
    {
        if (queueTarget <= 0)
            return queueTarget;

        var soft = Math.Max(0, softCap);
        var hard = Math.Max(soft + 1, hardCap);

        if (depth >= hard)
            return 0;
        if (depth <= soft)
            return queueTarget;

        var headroom = (double)(hard - depth) / (hard - soft);
        return Math.Max(0, (int)Math.Floor(queueTarget * headroom));
    }
}

public sealed class HangfireQueueDepthProbe(JobStorage jobStorage) : IQueueDepthProbe
{
    public long GetEnqueuedCount(string queue)
    {
        try
        {
            // Targeted COUNT on the jobqueue table (Hangfire.PostgreSql) — cheap relative to the
            // per-region match-coverage counts the producer already runs each cycle.
            return jobStorage.GetMonitoringApi().EnqueuedCount(queue);
        }
        catch
        {
            // Fail open: a monitoring hiccup must never stall ingestion. Returning 0 means "no
            // backpressure", which degrades safely back to the pre-backpressure behavior.
            return 0;
        }
    }
}
