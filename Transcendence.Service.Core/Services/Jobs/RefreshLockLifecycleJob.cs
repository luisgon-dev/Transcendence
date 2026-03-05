using Hangfire;
using Transcendence.Data.Repositories.Interfaces;

namespace Transcendence.Service.Core.Services.Jobs;

[DisableConcurrentExecution(timeoutInSeconds: 5 * 60)]
public class RefreshLockLifecycleJob(
    IRefreshLockRepository refreshLockRepository,
    ILogger<RefreshLockLifecycleJob> logger)
{
    private static readonly TimeSpan DefaultForensicsWindow = TimeSpan.FromMinutes(30);
    private const int DefaultCleanupBatchSize = 250;
    private const int DefaultMaxBatchesPerRun = 8;
    private const int CleanupBatchSizeCap = 1000;
    private const int MaxBatchesPerRunCap = 100;

    [Queue("refresh-low")]
    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        var forensicsWindow = DefaultForensicsWindow;
        var batchSize = Math.Min(DefaultCleanupBatchSize, CleanupBatchSizeCap);
        var maxBatches = Math.Min(DefaultMaxBatchesPerRun, MaxBatchesPerRunCap);
        var cutoffUtc = DateTime.UtcNow.Subtract(forensicsWindow);

        var batchesProcessed = 0;
        var totalDeleted = 0;

        logger.LogInformation(
            "[RefreshLockLifecycle] Starting cleanup run with cutoff={CutoffUtc:o}, batchSize={BatchSize}, maxBatches={MaxBatches}.",
            cutoffUtc,
            batchSize,
            maxBatches);

        try
        {
            while (!ct.IsCancellationRequested && batchesProcessed < maxBatches)
            {
                var deleted = await refreshLockRepository.DeleteExpiredAsync(cutoffUtc, batchSize, ct);
                if (deleted <= 0)
                    break;

                batchesProcessed++;
                totalDeleted += deleted;

                if (deleted < batchSize)
                    break;
            }

            var growthSnapshot = await refreshLockRepository.GetGrowthSnapshotAsync(DateTime.UtcNow, ct);
            logger.LogInformation(
                "[RefreshLockLifecycle] Cleanup completed. batchesProcessed={BatchesProcessed}, deleted={DeletedCount}, activeLocks={ActiveCount}, expiredLocks={ExpiredCount}.",
                batchesProcessed,
                totalDeleted,
                growthSnapshot.ActiveCount,
                growthSnapshot.ExpiredCount);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            logger.LogInformation(
                "[RefreshLockLifecycle] Cleanup canceled after {BatchesProcessed} batches and {DeletedCount} deletions.",
                batchesProcessed,
                totalDeleted);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "[RefreshLockLifecycle] Cleanup failed after {BatchesProcessed} batches and {DeletedCount} deletions. Continuing worker execution.",
                batchesProcessed,
                totalDeleted);
        }
    }
}
