using Camille.Enums;
using Hangfire;
using Transcendence.Data.Repositories.Interfaces;
using Transcendence.Service.Core.Services.Diagnostics;
using Transcendence.Service.Core.Services.Jobs;
using Transcendence.Service.Core.Services.Jobs.Interfaces;
using Transcendence.Service.Core.Services.Refresh.Interfaces;

namespace Transcendence.Service.Core.Services.Refresh.Implementations;

public sealed class SummonerRefreshCoordinator(
    IRefreshLockRepository refreshLockRepository,
    IBackgroundJobClient backgroundJobClient,
    IRefreshLockLifecycleTelemetry lockTelemetry,
    ILogger<SummonerRefreshCoordinator> logger) : ISummonerRefreshCoordinator
{
    private static readonly TimeSpan RefreshLockTtl = TimeSpan.FromMinutes(15);

    public async Task<RefreshEnqueueOutcome> EnqueueRefreshAsync(
        string gameName,
        string tagLine,
        PlatformRoute platform,
        string? pollUrl,
        string traceId,
        Guid? requestedByUserAccountId,
        string telemetrySource,
        CancellationToken ct = default)
    {
        var key = RefreshLockKeys.BuildSummonerRefreshKey(platform, gameName, tagLine);
        var priorityKey = RefreshLockKeys.BuildApiPriorityKey(platform, gameName, tagLine);
        var keyToken = await refreshLockRepository.TryAcquireOwnedAsync(key, RefreshLockTtl, ct);
        if (keyToken is null)
        {
            var existing = await refreshLockRepository.GetAsync(key, ct);
            var seconds = existing is null
                ? (int)RefreshLockTtl.TotalSeconds
                : (int)Math.Max(1, (existing.LockedUntilUtc - DateTime.UtcNow).TotalSeconds);
            EmitTelemetry(() =>
            {
                lockTelemetry.RecordLifecycleOutcome(key, "contention", telemetrySource);
                lockTelemetry.RecordContentionWaitHint(key, Math.Max(1, seconds), telemetrySource);
            });

            logger.LogInformation(
                "[RefreshApi] Summoner refresh already in progress for {GameName}#{Tag} on {Platform}. retryAfterSeconds={RetryAfterSeconds}, traceId={TraceId}.",
                gameName,
                tagLine,
                platform,
                seconds,
                traceId);

            return RefreshEnqueueOutcome.InProgress(pollUrl, seconds);
        }

        EmitTelemetry(() => lockTelemetry.RecordLifecycleOutcome(key, "acquired", telemetrySource));

        var priorityToken = await refreshLockRepository.TryAcquireOwnedAsync(priorityKey, RefreshLockTtl, ct);
        if (priorityToken is null)
        {
            EmitTelemetry(() =>
            {
                lockTelemetry.RecordLifecycleOutcome(priorityKey, "contention", telemetrySource);
                lockTelemetry.RecordContentionWaitHint(
                    priorityKey,
                    (int)RefreshLockTtl.TotalSeconds,
                    telemetrySource);
            });
        }

        try
        {
            var priorityHandle = priorityToken.HasValue
                ? RefreshLockKeys.BuildOwnedHandle(priorityKey, priorityToken.Value)
                : null;
            backgroundJobClient.Enqueue<ISummonerRefreshJob>(job =>
                job.RefreshByRiotId(
                    gameName,
                    tagLine,
                    platform,
                    RefreshLockKeys.BuildOwnedHandle(key, keyToken.Value),
                    priorityHandle,
                    requestedByUserAccountId,
                    CancellationToken.None));
        }
        catch (Exception ex)
        {
            await refreshLockRepository.ReleaseOwnedAsync(key, keyToken.Value, ct);
            if (priorityToken is not null)
                await refreshLockRepository.ReleaseOwnedAsync(priorityKey, priorityToken.Value, ct);

            logger.LogError(
                ex,
                "[RefreshApi] Failed to queue summoner refresh for {GameName}#{Tag} on {Platform}. priorityLockAcquired={PriorityLockAcquired}, traceId={TraceId}.",
                gameName,
                tagLine,
                platform,
                priorityToken is not null,
                traceId);
            throw;
        }

        logger.LogInformation(
            "[RefreshApi] Queued summoner refresh for {GameName}#{Tag} on {Platform}. priorityLockAcquired={PriorityLockAcquired}, traceId={TraceId}.",
            gameName,
            tagLine,
            platform,
            priorityToken is not null,
            traceId);

        return RefreshEnqueueOutcome.Queued(pollUrl);
    }

    public async Task<RefreshProgress?> GetProgressAsync(
        string gameName,
        string tagLine,
        PlatformRoute platform,
        string telemetrySource,
        CancellationToken ct = default)
    {
        var key = RefreshLockKeys.BuildSummonerRefreshKey(platform, gameName, tagLine);
        var existing = await refreshLockRepository.GetAsync(key, ct);
        if (existing is null || existing.LockedUntilUtc <= DateTime.UtcNow)
            return null;

        var seconds = Math.Max(1, (int)(existing.LockedUntilUtc - DateTime.UtcNow).TotalSeconds);
        EmitTelemetry(() =>
        {
            lockTelemetry.RecordLifecycleOutcome(key, "contention", telemetrySource);
            lockTelemetry.RecordContentionWaitHint(key, seconds, telemetrySource);
        });
        return new RefreshProgress(seconds);
    }

    private static void EmitTelemetry(Action emit)
    {
        try
        {
            emit();
        }
        catch
        {
            // Telemetry must never affect lock ownership or enqueue behavior.
        }
    }
}
