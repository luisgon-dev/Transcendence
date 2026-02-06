using Hangfire;
using Hangfire.Storage;

namespace Transcendence.Service.Core.Services.Extensions;

public static class HangfireExtensions
{
    public static void PurgeJobs(this IMonitoringApi monitor)
    {
        var toDelete = new List<string>();

        foreach (var queue in monitor.Queues())
            for (var i = 0; i < Math.Ceiling(queue.Length / 1000d); i++)
                monitor.EnqueuedJobs(queue.Name, 1000 * i, 1000)
                    .ForEach(x => toDelete.Add(x.Key));

        foreach (var jobId in toDelete) BackgroundJob.Delete(jobId);
    }

    public static int RemoveInvalidRecurringJobs(
        this JobStorage storage,
        ILogger logger,
        IEnumerable<string>? legacyRecurringJobIds = null,
        IEnumerable<string>? legacyTypeNameFragments = null)
    {
        ArgumentNullException.ThrowIfNull(storage);
        ArgumentNullException.ThrowIfNull(logger);

        var legacyIds = legacyRecurringJobIds?.ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
        var legacyTypeMarkers = legacyTypeNameFragments?.ToArray() ?? [];

        using var connection = storage.GetConnection();
        using var distributedLock = connection.AcquireDistributedLock("maintenance:recurring-job-cleanup",
            TimeSpan.FromSeconds(30));

        var recurringJobIds = connection.GetAllItemsFromSet("recurring-jobs") ?? [];
        if (recurringJobIds.Count == 0) return 0;

        using var tx = connection.CreateWriteTransaction();
        var removed = 0;

        foreach (var recurringJobId in recurringJobIds)
        {
            var hashKey = $"recurring-job:{recurringJobId}";
            var hash = connection.GetAllEntriesFromHash(hashKey);

            var shouldRemove = false;
            var reason = string.Empty;

            if (legacyIds.Contains(recurringJobId))
            {
                shouldRemove = true;
                reason = "legacy recurring job id";
            }
            else if (hash == null || hash.Count == 0)
            {
                shouldRemove = true;
                reason = "missing recurring job hash";
            }
            else if (!hash.TryGetValue("Job", out var jobPayload) || string.IsNullOrWhiteSpace(jobPayload))
            {
                shouldRemove = true;
                reason = "missing recurring job payload";
            }
            else if (legacyTypeMarkers.Length > 0 &&
                     legacyTypeMarkers.Any(marker =>
                         jobPayload.Contains(marker, StringComparison.OrdinalIgnoreCase)))
            {
                shouldRemove = true;
                reason = "legacy job type marker";
            }
            else
            {
                try
                {
                    var invocation = InvocationData.DeserializePayload(jobPayload);
                    _ = invocation.DeserializeJob();
                }
                catch (Exception ex)
                {
                    shouldRemove = true;
                    reason = $"unresolvable job payload ({ex.GetType().Name})";
                }
            }

            if (!shouldRemove) continue;

            tx.RemoveHash(hashKey);
            tx.RemoveFromSet("recurring-jobs", recurringJobId);
            removed++;

            logger.LogWarning("Removed recurring job {RecurringJobId} during startup cleanup: {Reason}.",
                recurringJobId, reason);
        }

        tx.Commit();
        return removed;
    }
}
