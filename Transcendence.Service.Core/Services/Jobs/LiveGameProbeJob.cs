using System.Text.Json;
using Transcendence.Data.Models.LiveGame;
using Transcendence.Data.Repositories.Interfaces;
using Transcendence.Service.Core.Services.Jobs.Interfaces;
using Transcendence.Service.Core.Services.LiveGame.Interfaces;
using Transcendence.Service.Core.Services.LiveGame.Models;
using Transcendence.Service.Core.Services.Operations;

namespace Transcendence.Service.Core.Services.Jobs;

public sealed class LiveGameProbeJob(
    ISummonerRepository summonerRepository,
    ILiveGamePollingService liveGamePollingService,
    ILiveGameSnapshotRepository snapshotRepository,
    IRefreshLockRepository refreshLockRepository,
    ILogger<LiveGameProbeJob> logger,
    IBackgroundOperationTracker operations) : ILiveGameProbeJob
{
    private static readonly TimeSpan LockReleaseTimeout = TimeSpan.FromSeconds(5);

    public async Task ProbeAsync(
        Guid operationId,
        string platformRegion,
        string gameName,
        string tagLine,
        string lockHandle,
        CancellationToken ct = default)
    {
        var (lockKey, ownerToken) = RefreshLockKeys.ParseOwnedHandle(lockHandle);
        try
        {
            if (!await operations.StartAsync(operationId, ct)) return;
            var summoner = await summonerRepository.FindByRiotIdAsync(
                platformRegion,
                gameName,
                tagLine,
                cancellationToken: ct);
            if (string.IsNullOrWhiteSpace(summoner?.Puuid))
            {
                await operations.FailAsync(operationId, "summoner_missing", ct);
                logger.LogInformation(
                    "Live-game probe skipped because {Region}/{GameName}#{TagLine} is not stored.",
                    platformRegion,
                    gameName,
                    tagLine);
                return;
            }

            var response = await liveGamePollingService.ProbeCurrentGameAsync(
                platformRegion,
                gameName,
                tagLine,
                ct);
            var observedAt = DateTime.UtcNow;
            var snapshotId = Guid.NewGuid();
            response = response with { LastUpdatedUtc = observedAt, DataAgeSeconds = 0 };
            await snapshotRepository.AddAsync(new LiveGameSnapshot
            {
                Id = snapshotId,
                SummonerId = summoner.Id,
                Puuid = summoner.Puuid,
                PlatformRegion = platformRegion,
                State = response.State,
                GameId = response.GameId,
                PayloadJson = JsonSerializer.Serialize(response),
                ObservedAtUtc = observedAt,
                NextPollAtUtc = observedAt.Add(LiveGamePollingState.GetNextInterval(response.State))
            }, ct);
            await snapshotRepository.SaveChangesAsync(ct);
            await operations.CompleteAsync(operationId, OperationStatuses.Succeeded, new OperationResult(
                SummonerId: summoner.Id, SnapshotId: snapshotId, ObservedAtUtc: observedAt, LiveGame: response), ct);
        }
        catch (Exception)
        {
            await operations.RetryAsync(operationId, "probe_failed", ct: CancellationToken.None);
            throw;
        }
        finally
        {
            using var releaseTimeout = new CancellationTokenSource(LockReleaseTimeout);
            try
            {
                if (ownerToken.HasValue)
                    await refreshLockRepository.ReleaseOwnedAsync(lockKey, ownerToken.Value, releaseTimeout.Token);
                else
                    await refreshLockRepository.ReleaseAsync(lockKey, releaseTimeout.Token);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Failed to release live-game probe lock {LockKey}.", lockKey);
            }
        }
    }
}
