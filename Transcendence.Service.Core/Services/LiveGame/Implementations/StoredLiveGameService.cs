using Transcendence.Data.Repositories.Interfaces;
using System.Text.Json;
using Transcendence.Service.Core.Services.LiveGame.Interfaces;
using Transcendence.Service.Core.Services.LiveGame.Models;

namespace Transcendence.Service.Core.Services.LiveGame.Implementations;

public class StoredLiveGameService(
    ISummonerRepository summonerRepository,
    ILiveGameSnapshotRepository snapshotRepository) : ILiveGameService
{
    public async Task<LiveGameResponseDto> GetCurrentGameAsync(
        string platformRegion,
        string gameName,
        string tagLine,
        CancellationToken ct = default)
    {
        var summoner = await summonerRepository.FindByRiotIdAsync(platformRegion, gameName, tagLine, cancellationToken: ct);
        if (string.IsNullOrWhiteSpace(summoner?.Puuid))
        {
            return BuildOfflineResponse(platformRegion);
        }

        var snapshot = await snapshotRepository.GetLatestByPuuidAsync(summoner.Puuid, platformRegion, ct);
        if (snapshot == null)
        {
            return BuildOfflineResponse(platformRegion);
        }

        var observedAtUtc = snapshot.ObservedAtUtc;
        if (!string.IsNullOrWhiteSpace(snapshot.PayloadJson))
        {
            try
            {
                var stored = JsonSerializer.Deserialize<LiveGameResponseDto>(snapshot.PayloadJson);
                if (stored != null)
                {
                    return stored with
                    {
                        LastUpdatedUtc = observedAtUtc,
                        DataAgeSeconds = Math.Max(0, (int)(DateTime.UtcNow - observedAtUtc).TotalSeconds)
                    };
                }
            }
            catch (JsonException)
            {
                // Legacy or partially written snapshots still retain the state/game fallback below.
            }
        }

        return new LiveGameResponseDto(
            snapshot.State,
            platformRegion,
            snapshot.GameId,
            null,
            null,
            null,
            null,
            [],
            observedAtUtc,
            Math.Max(0, (int)(DateTime.UtcNow - observedAtUtc).TotalSeconds));
    }

    private static LiveGameResponseDto BuildOfflineResponse(string platformRegion)
    {
        return new LiveGameResponseDto(
            "offline",
            platformRegion,
            null,
            null,
            null,
            null,
            null,
            [],
            DateTime.UtcNow,
            0);
    }
}
