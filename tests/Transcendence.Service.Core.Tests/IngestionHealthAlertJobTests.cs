using Transcendence.Service.Core.Services.Diagnostics;
using Transcendence.Service.Core.Services.Jobs;
using Xunit;

namespace Transcendence.Service.Core.Tests;

public class IngestionHealthAlertJobTests
{
    private static readonly AlertOptions Opts = new()
    {
        FailedJobSpikeThreshold = 50,
        DiscoveryQueueDepthThreshold = 10_000,
    };

    private static System.Collections.Generic.IReadOnlyList<(string Key, string Message)> Eval(
        long failed = 0,
        long? prevFailed = 0,
        long succeeded = 100,
        long? prevSucceeded = 100,
        long enqueued = 0,
        long discovery = 0)
        => IngestionHealthAlertJob.EvaluateConditions(
            failed, prevFailed, succeeded, prevSucceeded, enqueued, discovery, Opts);

    [Fact]
    public void Healthy_NoConditions()
    {
        Assert.Empty(Eval(failed: 5, prevFailed: 0, succeeded: 200, prevSucceeded: 100, enqueued: 10, discovery: 10));
    }

    [Fact]
    public void FailedSpike_FiresOnDelta()
    {
        // 60 new failures this interval > spike threshold 50.
        Assert.Contains(Eval(failed: 1060, prevFailed: 1000), c => c.Key == "failed-jobs");
    }

    [Fact]
    public void FailedJobs_DoesNotFireOnAccumulatedHistory()
    {
        // The prod scenario: 2743 total retained, but only 3 new since the last check → no alert.
        Assert.DoesNotContain(Eval(failed: 2743, prevFailed: 2740), c => c.Key == "failed-jobs");
    }

    [Fact]
    public void FailedJobs_DoesNotFireOnFirstRun()
    {
        // No baseline yet → cannot compute a delta → no alert even with a huge retained count.
        Assert.DoesNotContain(Eval(failed: 2743, prevFailed: null), c => c.Key == "failed-jobs");
    }

    [Fact]
    public void DiscoveryBacklog_Fires()
    {
        Assert.Contains(Eval(discovery: 10_001), c => c.Key == "discovery-backlog");
    }

    [Fact]
    public void ThroughputStall_FiresWhenNoCompletionsAndWorkPending()
    {
        Assert.Contains(Eval(succeeded: 100, prevSucceeded: 100, enqueued: 50), c => c.Key == "throughput-stall");
    }

    [Fact]
    public void ThroughputStall_DoesNotFireOnFirstRun()
    {
        Assert.DoesNotContain(Eval(succeeded: 100, prevSucceeded: null, enqueued: 50), c => c.Key == "throughput-stall");
    }

    [Fact]
    public void ThroughputStall_DoesNotFireWhenCompletionsAdvance()
    {
        Assert.DoesNotContain(Eval(succeeded: 150, prevSucceeded: 100, enqueued: 50), c => c.Key == "throughput-stall");
    }

    [Fact]
    public void ThroughputStall_DoesNotFireWhenNothingEnqueued()
    {
        Assert.DoesNotContain(Eval(succeeded: 100, prevSucceeded: 100, enqueued: 0, discovery: 0), c => c.Key == "throughput-stall");
    }
}
