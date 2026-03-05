using Hangfire;
using Microsoft.Extensions.Options;
using Transcendence.Data.Repositories.Interfaces;
using Transcendence.Service.Core.Services.Jobs.Configuration;

namespace Transcendence.Service.Core.Services.Jobs;

[DisableConcurrentExecution(timeoutInSeconds: 5 * 60)]
public class RefreshLockLifecycleJob(
    IRefreshLockRepository refreshLockRepository,
    IOptions<WorkerJobScheduleOptions> scheduleOptionsAccessor,
    ILogger<RefreshLockLifecycleJob> logger)
{
    private const int CleanupBatchSizeCap = 1000;
    private const int MaxBatchesPerRunCap = 100;
    private const int ForensicsWindowMinutesCap = 24 * 60;

    [Queue("refresh-low")]
    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        var schedule = scheduleOptionsAccessor.Value;
        if (!schedule.EnableRefreshLockLifecycleCleanup)
        {
            logger.LogInformation("[RefreshLockLifecycle] Cleanup skipped because scheduling toggle is disabled.");
            return;
        }

        var forensicsWindowMinutes = Math.Clamp(
            schedule.RefreshLockLifecycleForensicsWindowMinutes,
            1,
            ForensicsWindowMinutesCap);
        var batchSize = Math.Clamp(
            schedule.RefreshLockLifecycleCleanupBatchSize,
            1,
            CleanupBatchSizeCap);
        var maxBatches = Math.Clamp(
            schedule.RefreshLockLifecycleCleanupMaxBatchesPerRun,
            1,
            MaxBatchesPerRunCap);
        var cutoffUtc = DateTime.UtcNow.AddMinutes(-forensicsWindowMinutes);

        var batchesProcessed = 0;
        var totalDeleted = 0;
        var stoppedByBatchCap = false;

        logger.LogInformation(
            "[RefreshLockLifecycle] Starting cleanup run with cutoff={CutoffUtc:o}, forensicsWindowMinutes={ForensicsWindowMinutes}, batchSize={BatchSize}, maxBatches={MaxBatches}.",
            cutoffUtc,
            forensicsWindowMinutes,
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

            stoppedByBatchCap = batchesProcessed >= maxBatches;
            var growthSnapshot = await refreshLockRepository.GetGrowthSnapshotAsync(DateTime.UtcNow, ct);
            logger.LogInformation(
                "[RefreshLockLifecycle] Cleanup completed. batchesProcessed={BatchesProcessed}, deleted={DeletedCount}, stoppedByBatchCap={StoppedByBatchCap}, activeLocks={ActiveCount}, expiredLocks={ExpiredCount}.",
                batchesProcessed,
                totalDeleted,
                stoppedByBatchCap,
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
