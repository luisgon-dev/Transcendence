using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Transcendence.Service.Core.Services.Diagnostics;

namespace Transcendence.Service.Core.Tests;

public sealed class WorkerHeartbeatTests
{
    [Fact]
    public async Task PublishingDoesNotInventProgress_AndHealthcheckMtimeTracksTheActualBeat()
    {
        var path = Path.Combine(Path.GetTempPath(), $"trn-heartbeat-{Guid.NewGuid():N}");
        var heartbeat = new WorkerHeartbeat(Mock.Of<IDistributedCache>(),
            Options.Create(new WorkerWatchdogOptions { HeartbeatFilePath = path }),
            NullLogger<WorkerHeartbeat>.Instance);
        try
        {
            await heartbeat.PublishAsync();
            Assert.False(File.Exists(path));
            Assert.Null(heartbeat.LastBeatUtc);

            new WorkerProgressFilter(heartbeat).OnPerforming(null!);
            var started = heartbeat.LastBeatUtc;
            Assert.NotNull(started);
            await heartbeat.PublishAsync();
            await Task.Delay(30);
            await heartbeat.PublishAsync();
            Assert.Equal(started, heartbeat.LastBeatUtc);
            Assert.Equal(started, DateTime.Parse(await File.ReadAllTextAsync(path)).ToUniversalTime());
            Assert.InRange((File.GetLastWriteTimeUtc(path) - started.Value).Duration(), TimeSpan.Zero, TimeSpan.FromMilliseconds(1));

            new WorkerProgressFilter(heartbeat).OnPerformed(null!);
            Assert.True(heartbeat.LastBeatUtc >= started);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
