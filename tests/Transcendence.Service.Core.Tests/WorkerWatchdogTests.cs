using Transcendence.Service.Core.Services.Diagnostics;
using Xunit;

namespace Transcendence.Service.Core.Tests;

public class WorkerWatchdogTests
{
    private static readonly TimeSpan Threshold = TimeSpan.FromMinutes(10);
    private static readonly DateTime Now = new(2026, 6, 19, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ShouldRestart_NeverBeat_IsFalse()
    {
        // Disarmed until the first beat, so broken wiring / a disabled producer can't crash-loop.
        Assert.False(WorkerWatchdogService.ShouldRestart(null, Now, Threshold));
    }

    [Fact]
    public void ShouldRestart_RecentBeat_IsFalse()
    {
        Assert.False(WorkerWatchdogService.ShouldRestart(Now.AddMinutes(-3), Now, Threshold));
    }

    [Fact]
    public void ShouldRestart_StaleBeat_IsTrue()
    {
        Assert.True(WorkerWatchdogService.ShouldRestart(Now.AddMinutes(-11), Now, Threshold));
    }

    [Fact]
    public void ShouldRestart_ExactlyAtThreshold_IsFalse()
    {
        // Strictly greater-than, so the boundary is not a restart.
        Assert.False(WorkerWatchdogService.ShouldRestart(Now - Threshold, Now, Threshold));
    }
}
