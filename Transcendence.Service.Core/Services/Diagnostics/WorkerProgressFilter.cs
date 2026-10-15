using Hangfire.Common;
using Hangfire.Server;
using Microsoft.Extensions.Hosting;

namespace Transcendence.Service.Core.Services.Diagnostics;

/// <summary>
/// Liveness follows work across every queue, including failed jobs that returned control.
/// One blocked ingestion dispatcher must not restart other pools that are making progress.
/// </summary>
public sealed class WorkerProgressFilter(IWorkerHeartbeat heartbeat) : JobFilterAttribute, IServerFilter
{
    public void OnPerforming(PerformingContext context) => heartbeat.RecordProgress();
    public void OnPerformed(PerformedContext context) => heartbeat.RecordProgress();
}

/// <summary>Publishes actual progress; timer ticks never create a new heartbeat.</summary>
public sealed class WorkerHeartbeatPublisher(IWorkerHeartbeat heartbeat) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            await heartbeat.PublishAsync(timeout.Token);
        }
    }
}
