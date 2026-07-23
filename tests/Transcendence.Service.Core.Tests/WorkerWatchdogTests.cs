using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
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

    [Fact]
    public async Task StaleHeartbeat_RequestsGracefulHostStopBeforeFallback()
    {
        var heartbeat = new Mock<IWorkerHeartbeat>();
        heartbeat.SetupGet(value => value.LastBeatUtc).Returns(DateTime.UtcNow.AddMinutes(-5));
        var stopRequested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var lifetime = new Mock<IHostApplicationLifetime>();
        lifetime.Setup(value => value.StopApplication()).Callback(() => stopRequested.SetResult());
        using var watchdog = new WorkerWatchdogService(
            heartbeat.Object,
            Options.Create(new WorkerWatchdogOptions
            {
                CheckInterval = TimeSpan.FromMilliseconds(10),
                StalenessThreshold = TimeSpan.FromSeconds(1),
                GracefulShutdownTimeout = TimeSpan.FromSeconds(5)
            }),
            lifetime.Object,
            NullLogger<WorkerWatchdogService>.Instance);

        await watchdog.StartAsync(CancellationToken.None);
        await stopRequested.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await watchdog.StopAsync(CancellationToken.None);

        lifetime.Verify(value => value.StopApplication(), Times.Once);
    }
}
