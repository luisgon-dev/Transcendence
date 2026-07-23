using System.Text.Json;
using Transcendence.Data.Models.LiveGame;
using Transcendence.Data.Repositories.Interfaces;
using Transcendence.Service.Core.Services.Jobs.Interfaces;
using Transcendence.Service.Core.Services.LiveGame.Interfaces;
using Transcendence.Service.Core.Services.LiveGame.Models;

namespace Transcendence.Service.Core.Services.Jobs;

public sealed class LiveGameProbeJob(
    ISummonerRepository summonerRepository,
    ILiveGamePollingService liveGamePollingService,
    ILiveGameSnapshotRepository snapshotRepository,
    IRefreshLockRepository refreshLockRepository,
    ILogger<LiveGameProbeJob> logger) : ILiveGameProbeJob
{
    private static readonly TimeSpan LockReleaseTimeout = TimeSpan.FromSeconds(5);

    public async Task ProbeAsync(
        string platformRegion,
        string gameName,
        string tagLine,
        string lockHandle,
        CancellationToken ct = default)
    {
        var (lockKey, ownerToken) = RefreshLockKeys.ParseOwnedHandle(lockHandle);
        try
        {
            var summoner = await summonerRepository.FindByRiotIdAsync(
                platformRegion,
                gameName,
                tagLine,
                cancellationToken: ct);
            if (string.IsNullOrWhiteSpace(summoner?.Puuid))
            {
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
            await snapshotRepository.AddAsync(new LiveGameSnapshot
            {
                Id = Guid.NewGuid(),
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
