using System.Linq;
using Transcendence.Service.Core.Services.Diagnostics;
using Transcendence.Service.Core.Services.Jobs;
using Xunit;

namespace Transcendence.Service.Core.Tests;

public class IngestionHealthAlertJobTests
{
    private static readonly AlertOptions Opts = new()
    {
        FailedJobThreshold = 200,
        DiscoveryQueueDepthThreshold = 10_000,
    };

    private static System.Collections.Generic.IReadOnlyList<(string Key, string Message)> Eval(
        long failed = 0, long succeeded = 100, long? prev = 100, long enqueued = 0, long discovery = 0)
        => IngestionHealthAlertJob.EvaluateConditions(failed, succeeded, prev, enqueued, discovery, Opts);

    [Fact]
    public void Healthy_NoConditions()
    {
        Assert.Empty(Eval(failed: 5, succeeded: 200, prev: 100, enqueued: 10, discovery: 10));
    }

    [Fact]
    public void FailedSpike_Fires()
    {
        Assert.Contains(Eval(failed: 201), c => c.Key == "failed-jobs");
    }

    [Fact]
    public void DiscoveryBacklog_Fires()
    {
        Assert.Contains(Eval(discovery: 10_001), c => c.Key == "discovery-backlog");
    }

    [Fact]
    public void ThroughputStall_FiresWhenNoCompletionsAndWorkPending()
    {
        // succeeded unchanged since last check (prev == succeeded) while work is enqueued.
        Assert.Contains(Eval(succeeded: 100, prev: 100, enqueued: 50), c => c.Key == "throughput-stall");
    }

    [Fact]
    public void ThroughputStall_DoesNotFireOnFirstRun()
    {
        // No previous sample yet → no stall signal even with work enqueued.
        Assert.DoesNotContain(Eval(succeeded: 100, prev: null, enqueued: 50), c => c.Key == "throughput-stall");
    }

    [Fact]
    public void ThroughputStall_DoesNotFireWhenCompletionsAdvance()
    {
        Assert.DoesNotContain(Eval(succeeded: 150, prev: 100, enqueued: 50), c => c.Key == "throughput-stall");
    }

    [Fact]
    public void ThroughputStall_DoesNotFireWhenNothingEnqueued()
    {
        Assert.DoesNotContain(Eval(succeeded: 100, prev: 100, enqueued: 0, discovery: 0), c => c.Key == "throughput-stall");
    }
}
