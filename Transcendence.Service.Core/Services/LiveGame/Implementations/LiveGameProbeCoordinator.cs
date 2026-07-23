using Camille.Enums;
using Hangfire;
using Transcendence.Data.Repositories.Interfaces;
using Transcendence.Service.Core.Services.Jobs;
using Transcendence.Service.Core.Services.Jobs.Interfaces;
using Transcendence.Service.Core.Services.LiveGame.Interfaces;
using Transcendence.Service.Core.Services.LiveGame.Models;

namespace Transcendence.Service.Core.Services.LiveGame.Implementations;

public sealed class LiveGameProbeCoordinator(
    IRefreshLockRepository refreshLockRepository,
    IBackgroundJobClient backgroundJobClient,
    ILogger<LiveGameProbeCoordinator> logger) : ILiveGameProbeCoordinator
{
    private static readonly TimeSpan ProbeLockTtl = TimeSpan.FromMinutes(1);
    private const int PollDelaySeconds = 2;

    public async Task<LiveGameProbeOutcome> EnqueueAsync(
        PlatformRoute platform,
        string gameName,
        string tagLine,
        CancellationToken ct = default)
    {
        var lockKey = RefreshLockKeys.BuildLiveGameProbeKey(platform, gameName, tagLine);
        var ownerToken = await refreshLockRepository.TryAcquireOwnedAsync(lockKey, ProbeLockTtl, ct);
        if (ownerToken is null)
            return new LiveGameProbeOutcome(false, PollDelaySeconds);

        try
        {
            backgroundJobClient.Enqueue<ILiveGameProbeJob>(job => job.ProbeAsync(
                platform.ToString(),
                gameName,
                tagLine,
                RefreshLockKeys.BuildOwnedHandle(lockKey, ownerToken.Value),
                CancellationToken.None));
        }
        catch
        {
            using var releaseTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try
            {
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

        return new LiveGameProbeOutcome(true, PollDelaySeconds);
    }
}
