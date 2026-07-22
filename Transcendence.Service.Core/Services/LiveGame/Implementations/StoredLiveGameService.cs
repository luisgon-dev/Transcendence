using Transcendence.Data.Repositories.Interfaces;
using System.Text.Json;
using Transcendence.Service.Core.Services.LiveGame.Interfaces;
using Transcendence.Service.Core.Services.LiveGame.Models;
using Transcendence.Service.Core.Services.RiotApi;

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
        if (!PlatformRouteParser.TryParse(platformRegion, out var platform))
            throw new ArgumentException($"Unsupported platform region '{platformRegion}'.", nameof(platformRegion));

        var normalizedRegion = platform.ToString();
        var summoner = await summonerRepository.FindByRiotIdAsync(
            normalizedRegion,
            gameName,
            tagLine,
            cancellationToken: ct);
        if (string.IsNullOrWhiteSpace(summoner?.Puuid))
        {
            return BuildOfflineResponse(normalizedRegion);
        }

        var snapshot = await snapshotRepository.GetLatestByPuuidAsync(summoner.Puuid, normalizedRegion, ct);
        if (snapshot == null)
        {
            return BuildOfflineResponse(normalizedRegion);
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
            normalizedRegion,
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
