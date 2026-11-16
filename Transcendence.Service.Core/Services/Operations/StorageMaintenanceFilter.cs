using Hangfire.Common;
using Hangfire.Server;
using Microsoft.Extensions.Options;

namespace Transcendence.Service.Core.Services.Operations;

/// <summary>Periodic disk-heavy work may yield to backups; foreground refresh/import jobs are unmarked.</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class StorageMaintenanceWorkAttribute : Attribute;

public sealed class StorageMaintenanceFilter : JobFilterAttribute, IServerFilter
{
    private const string LeaseKey = "transcendence.storage-maintenance.lease";
    private readonly IOptions<MatchArchiveOptions> options;
    private readonly ILogger<StorageMaintenanceFilter> logger;

    public StorageMaintenanceFilter(IOptions<MatchArchiveOptions> options, ILogger<StorageMaintenanceFilter> logger)
    {
        this.options = options;
        this.logger = logger;
        // Acquire storage only after DisableConcurrentExecution has acquired its job lock.
        // Duplicate jobs waiting on that lock must not hold a shared storage lease.
        Order = 10;
    }

    public void OnPerforming(PerformingContext context)
    {
        if (!UsesGate(context.BackgroundJob.Job.Type)) return;
        var directory = options.Value.MaintenanceDirectory;
        if (!File.Exists(Path.Combine(directory, "run.lock")))
        {
            if (options.Value.Enabled) throw new InvalidOperationException("Storage maintenance volume is missing");
            return; // Local worker development without the optional host maintenance services.
        }
        var lease = ArchiveMaintenanceLease.TryAcquire(directory, exclusive: false);
        if (lease is null)
        {
            context.Canceled = true;
            logger.LogInformation("Deferred periodic {JobType} while storage maintenance owns the gate; the next recurrence retries.", context.BackgroundJob.Job.Type.Name);
            return;
        }
        context.Items[LeaseKey] = lease;
    }

    public void OnPerformed(PerformedContext context)
    {
        if (context.Items.Remove(LeaseKey, out var lease)) (lease as IDisposable)?.Dispose();
    }

    internal static bool UsesGate(Type jobType) => Attribute.IsDefined(jobType, typeof(StorageMaintenanceWorkAttribute), inherit: true);
}
