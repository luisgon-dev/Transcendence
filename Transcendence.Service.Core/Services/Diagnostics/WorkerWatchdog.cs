using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Transcendence.Service.Core.Services.Diagnostics;

public sealed class WorkerWatchdogOptions
{
    /// <summary>
    /// When true, the watchdog requests host shutdown once the producer heartbeat goes stale past
    /// <see cref="StalenessThreshold"/> and uses a hard process exit only if graceful shutdown cannot
    /// complete. A Docker HEALTHCHECK only marks a container unhealthy — it does not restart it.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// How stale the last producer beat may get before the worker is considered hung. Must
    /// comfortably exceed the producer cadence (ChampionAnalyticsIngestion fires every 2 min).
    /// </summary>
    public TimeSpan StalenessThreshold { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>How often the watchdog re-checks heartbeat freshness.</summary>
    public TimeSpan CheckInterval { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Maximum time allowed for the generic host to stop cleanly after a stale heartbeat before
    /// the watchdog uses the container-restart fallback.
    /// </summary>
    public TimeSpan GracefulShutdownTimeout { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>Path to the heartbeat file the container HEALTHCHECK reads.</summary>
    public string HeartbeatFilePath { get; set; } = "/tmp/worker-heartbeat";
}

/// <summary>
/// Watches the producer heartbeat on a dedicated thread — not a thread-pool timer — so it keeps
/// running even when the managed thread pool is exhausted (the documented worker-hang class). When
/// the heartbeat goes stale it first requests a bounded generic-host shutdown, then exits only as a
/// fallback so the container restart policy still recovers a truly wedged host. It arms only after
/// the first beat, so broken wiring or a deliberately-disabled producer can never drive a restart
/// loop (a never-beating worker is left alone).
/// </summary>
public sealed class WorkerWatchdogService(
    IWorkerHeartbeat heartbeat,
    IOptions<WorkerWatchdogOptions> options,
    IHostApplicationLifetime applicationLifetime,
    ILogger<WorkerWatchdogService> logger) : IHostedService, IDisposable
{
    private readonly CancellationTokenSource _stopping = new();
    private Thread? _thread;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var opts = options.Value;
        if (!opts.Enabled)
        {
            logger.LogInformation("Worker watchdog disabled (Worker:Watchdog:Enabled=false).");
            return Task.CompletedTask;
        }

        _thread = new Thread(() => Loop(opts))
        {
            IsBackground = true,
            Name = "worker-watchdog",
        };
        _thread.Start();
        logger.LogInformation(
            "Worker watchdog armed: staleness threshold {Threshold}, check interval {Interval}.",
            opts.StalenessThreshold,
            opts.CheckInterval);
        return Task.CompletedTask;
    }

    private void Loop(WorkerWatchdogOptions opts)
    {
        var token = _stopping.Token;
        while (!token.IsCancellationRequested)
        {
            // WaitHandle (not Thread.Sleep) so a graceful stop wakes the loop immediately.
            token.WaitHandle.WaitOne(opts.CheckInterval);
            if (token.IsCancellationRequested)
            {
                return;
            }

            var last = heartbeat.LastBeatUtc;
            if (!ShouldRestart(last, DateTime.UtcNow, opts.StalenessThreshold))
            {
                continue;
            }

            logger.LogCritical(
                "Worker watchdog: last producer beat {LastBeatUtc:o} is older than {Threshold}; the worker " +
                "appears hung. Requesting graceful host shutdown before the hard-exit fallback.",
                last,
                opts.StalenessThreshold);

            applicationLifetime.StopApplication();
            if (token.WaitHandle.WaitOne(opts.GracefulShutdownTimeout))
                return;

            logger.LogCritical(
                "Worker watchdog: graceful shutdown did not complete within {Timeout}; forcing exit so " +
                "the container restart policy recreates the worker.",
                opts.GracefulShutdownTimeout);
            Environment.Exit(70); // EX_SOFTWARE — final fallback for a host that cannot stop.
        }
    }

    /// <summary>
    /// Pure decision: restart only once a beat has occurred (armed) and the last beat has since
    /// aged past the staleness threshold.
    /// </summary>
    public static bool ShouldRestart(DateTime? lastBeatUtc, DateTime nowUtc, TimeSpan stalenessThreshold)
        => lastBeatUtc.HasValue && (nowUtc - lastBeatUtc.Value) > stalenessThreshold;

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _stopping.Cancel();
        if (_thread is { IsAlive: true } && Thread.CurrentThread != _thread)
            _thread.Join(TimeSpan.FromSeconds(1));
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _stopping.Dispose();
    }
}
