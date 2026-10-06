using Camille.Enums;
using Hangfire;
using Transcendence.Data.Repositories.Interfaces;
using Transcendence.Service.Core.Services.Jobs;
using Transcendence.Service.Core.Services.Jobs.Interfaces;
using Transcendence.Service.Core.Services.LiveGame.Interfaces;
using Transcendence.Service.Core.Services.LiveGame.Models;
using Transcendence.Service.Core.Services.Operations;

namespace Transcendence.Service.Core.Services.LiveGame.Implementations;

public sealed class LiveGameProbeCoordinator(
    IRefreshLockRepository refreshLockRepository,
    IBackgroundOperationTracker operations,
    IBackgroundOperationDispatcher dispatcher,
    ILogger<LiveGameProbeCoordinator> logger) : ILiveGameProbeCoordinator
{
    private static readonly TimeSpan ProbeLockTtl = TimeSpan.FromMinutes(1);
    private const int PollDelaySeconds = 2;

    public async Task<LiveGameProbeOutcome> EnqueueAsync(
        PlatformRoute platform,
        string gameName,
        string tagLine,
        OperationOwner owner,
        CancellationToken ct = default)
    {
        var lockKey = RefreshLockKeys.BuildLiveGameProbeKey(platform, gameName, tagLine);
        var ownerToken = await refreshLockRepository.TryAcquireOwnedAsync(lockKey, ProbeLockTtl, ct);
        if (ownerToken is null)
        {
            var lease = await refreshLockRepository.GetAsync(lockKey, ct);
            if (lease?.OwnerToken is not { } token)
                throw new InvalidOperationException("Probe lease changed; retry the request.");
            var joined = await operations.JoinAsync(lockKey, token, owner, ct);
            return new LiveGameProbeOutcome(false, joined.OperationId, PollDelaySeconds);
        }

        TrackedOperation? tracked = null;
        try
        {
            tracked = await operations.CreateAsync(OperationKinds.LiveGameProbe, lockKey,
                new OperationResource(platform.ToString(), gameName.Trim(), tagLine.Trim()), ownerToken.Value, owner,
                new OperationDispatch(RefreshLockKeys.BuildOwnedHandle(lockKey, ownerToken.Value), null, null), ct);
        }
        catch
        {
            using var releaseTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try
            {
                if (tracked is not null) await operations.FailAsync(tracked.ExecutionId, "enqueue_failed", releaseTimeout.Token);
                await refreshLockRepository.ReleaseOwnedAsync(lockKey, ownerToken.Value, releaseTimeout.Token);
            }
            catch (Exception releaseException)
            {
                logger.LogWarning(
                    releaseException,
                    "Failed to release live-game probe lock {LockKey} after enqueue failure.",
                    lockKey);
            }

            throw;
        }

        try { await dispatcher.DispatchAsync(tracked.ExecutionId, ct); }
        catch (Exception exception) { logger.LogWarning(exception, "Durable probe dispatch deferred for operation {OperationId}.", tracked.ExecutionId); }

        return new LiveGameProbeOutcome(true, tracked.OperationId, PollDelaySeconds);
    }
}
