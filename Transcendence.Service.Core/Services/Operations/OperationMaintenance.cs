using System.Diagnostics.Metrics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Transcendence.Data;

namespace Transcendence.Service.Core.Services.Operations;

public sealed class OperationRetentionOptions
{
    public int CompletedDays { get; set; } = 7;
    public int ExpiredTokenDays { get; set; } = 1;
    public int BatchSize { get; set; } = 500;
    public int IntervalMinutes { get; set; } = 60;
}

public sealed class OperationTelemetry : IDisposable
{
    public const string MeterName = "Transcendence.Operations";
    private readonly Meter meter = new(MeterName);
    private readonly Histogram<double> dispatchLag;
    private readonly Counter<long> deleted;
    private long pending;
    private double oldestAge;
    private double lastScan;
    public OperationTelemetry()
    {
        dispatchLag = meter.CreateHistogram<double>("operation.dispatch.lag", "s");
        deleted = meter.CreateCounter<long>("operation.retention.deleted", "{row}");
        meter.CreateObservableGauge("operation.pending", () => Interlocked.Read(ref pending), "{operation}");
        meter.CreateObservableGauge("operation.pending.oldest_age", () => Volatile.Read(ref oldestAge), "s");
        meter.CreateObservableGauge("operation.scan.last_success", () => Volatile.Read(ref lastScan), "s");
    }
    public void Scan(long count, DateTime? oldest)
    {
        Interlocked.Exchange(ref pending, count);
        Volatile.Write(ref oldestAge, oldest is null ? 0 : Math.Max(0, (DateTime.UtcNow - oldest.Value).TotalSeconds));
        Volatile.Write(ref lastScan, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
    }
    public void Dispatched(DateTime queued) => dispatchLag.Record(Math.Max(0, (DateTime.UtcNow - queued).TotalSeconds));
    public void Deleted(string kind, int count) => deleted.Add(count, new KeyValuePair<string, object?>("kind", kind));
    public void Dispose() => meter.Dispose();
}

/// <summary>One bounded batch per table; active operations and unexpired replay evidence survive.</summary>
public sealed class OperationMaintenance(TranscendenceContext db, IOptions<OperationRetentionOptions> options,
    OperationTelemetry telemetry)
{
    public async Task RunBatchAsync(DateTime now, CancellationToken ct = default)
    {
        var batch = Math.Clamp(options.Value.BatchSize, 1, 5000);
        var completedBefore = now.AddDays(-Math.Clamp(options.Value.CompletedDays, 1, 365));
        var expiredBefore = now.AddDays(-Math.Clamp(options.Value.ExpiredTokenDays, 0, 30));
        // Leaf-first deletion keeps a parent's authorization boundary while a child still exists.
        var operations = db.BackgroundOperations.Where(x =>
            (x.Status == OperationStatuses.Succeeded || x.Status == OperationStatuses.Partial || x.Status == OperationStatuses.Failed)
            && x.CompletedAtUtc < completedBefore && !db.BackgroundOperations.Any(child => child.ParentId == x.Id))
            .OrderBy(x => x.CompletedAtUtc).Select(x => x.Id).Take(batch);
        telemetry.Deleted("operation", await db.BackgroundOperations.Where(x => operations.Contains(x.Id)).ExecuteDeleteAsync(ct));
        var refresh = db.UserRefreshTokens.Where(x => x.ExpiresAtUtc < expiredBefore)
            .OrderBy(x => x.ExpiresAtUtc).Select(x => x.Id).Take(batch);
        telemetry.Deleted("refresh", await db.UserRefreshTokens.Where(x => refresh.Contains(x.Id)).ExecuteDeleteAsync(ct));
        var reset = db.UserPasswordResetTokens.Where(x => x.ExpiresAtUtc < expiredBefore)
            .OrderBy(x => x.ExpiresAtUtc).Select(x => x.Id).Take(batch);
        telemetry.Deleted("reset", await db.UserPasswordResetTokens.Where(x => reset.Contains(x.Id)).ExecuteDeleteAsync(ct));
        // Ciphertext is needed only for the brief concurrency window. Hash/family remain for replay detection.
        var recoveryBefore = now.AddSeconds(-10);
        var recovery = db.UserRefreshTokens.Where(x => x.ProtectedReplacementToken != null && x.RevokedAtUtc < recoveryBefore)
            .OrderBy(x => x.RevokedAtUtc).Select(x => x.Id).Take(batch);
        telemetry.Deleted("recovery_ciphertext", await db.UserRefreshTokens.Where(x => recovery.Contains(x.Id))
            .ExecuteUpdateAsync(set => set.SetProperty(x => x.ProtectedReplacementToken, (string?)null), ct));
    }
}

public sealed class OperationMaintenanceService(IServiceScopeFactory scopes, IOptions<OperationRetentionOptions> options,
    ILogger<OperationMaintenanceService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(Math.Clamp(options.Value.IntervalMinutes, 1, 1440)));
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<OperationMaintenance>().RunBatchAsync(DateTime.UtcNow, stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            { logger.LogWarning(ex, "Operation retention batch deferred."); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
