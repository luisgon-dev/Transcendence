using Camille.Enums;
using Camille.RiotGames;
using Camille.RiotGames.Util;
using Microsoft.Extensions.Caching.Hybrid;
using Transcendence.Data.Repositories.Interfaces;
using Transcendence.Service.Core.Services.LiveGame.Interfaces;
using Transcendence.Service.Core.Services.LiveGame.Models;

namespace Transcendence.Service.Core.Services.LiveGame.Implementations;

public class LiveGameService(
    RiotGamesApi riotApi,
    ISummonerRepository summonerRepository,
    HybridCache cache,
    ILogger<LiveGameService> logger) : ILiveGameService
{
    private static readonly HybridCacheEntryOptions LiveGameCacheOptions = new()
    {
        Expiration = TimeSpan.FromMinutes(2),
        LocalCacheExpiration = TimeSpan.FromSeconds(30)
    };

    public async Task<LiveGameResponseDto> GetCurrentGameAsync(
        string platformRegion,
        string gameName,
        string tagLine,
        CancellationToken ct = default)
    {
        if (!TryParsePlatformRoute(platformRegion, out var platform))
            throw new ArgumentException($"Unsupported platform region '{platformRegion}'.", nameof(platformRegion));

        var normalizedRegion = platform.ToString();
        var normalizedGameName = gameName.Trim();
        var normalizedTagLine = tagLine.Trim();

        var cacheKey =
            $"livegame:{normalizedRegion}:{normalizedGameName.ToUpperInvariant()}:{normalizedTagLine.ToUpperInvariant()}";

        return await cache.GetOrCreateAsync(
            cacheKey,
            async cancel =>
            {
                var puuid = await ResolvePuuidAsync(platform, normalizedGameName, normalizedTagLine, cancel);
                if (string.IsNullOrWhiteSpace(puuid))
                {
                    return BuildOfflineResponse(normalizedRegion);
                }

                try
                {
                    var gameInfo = await riotApi.SpectatorV5()
                        .GetCurrentGameInfoByPuuidAsync(platform, puuid, cancel);

                    return new LiveGameResponseDto(
                        State: "in_game",
                        PlatformRegion: normalizedRegion,
                        GameId: gameInfo.GameId.ToString(),
                        QueueType: gameInfo.GameQueueConfigId?.ToString(),
                        Map: gameInfo.MapId.ToString(),
                        GameStartTimeUtc: DateTimeOffset.FromUnixTimeMilliseconds(gameInfo.GameStartTime).UtcDateTime,
                        GameLengthSeconds: gameInfo.GameLength,
                        Participants: gameInfo.Participants.Select(p => new LiveGameParticipantDto(
                            Puuid: p.Puuid ?? string.Empty,
                            RiotId: p.RiotId,
                            SummonerId: p.SummonerId,
                            TeamId: (int)p.TeamId,
                            ChampionId: (int)p.ChampionId,
                            Spell1Id: (int)p.Spell1Id,
                            Spell2Id: (int)p.Spell2Id,
                            ProfileIconId: (int)p.ProfileIconId
                        )).ToList(),
                        LastUpdatedUtc: DateTime.UtcNow,
                        DataAgeSeconds: 0
                    );
                }
                catch (RiotResponseException ex) when (ex.GetResponse().StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    return BuildOfflineResponse(normalizedRegion);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex,
                        "Failed to fetch live game for {Region}/{GameName}#{TagLine}",
                        normalizedRegion,
                        normalizedGameName,
                        normalizedTagLine);
                    throw;
                }
            },
            LiveGameCacheOptions,
            tags: ["live-game"],
            cancellationToken: ct
        );
    }

    private async Task<string?> ResolvePuuidAsync(
        PlatformRoute platform,
        string gameName,
        string tagLine,
        CancellationToken ct)
    {
        var summoner = await summonerRepository.FindByRiotIdAsync(
            platform.ToString(),
            gameName,
            tagLine,
            cancellationToken: ct
        );

        if (!string.IsNullOrWhiteSpace(summoner?.Puuid))
            return summoner.Puuid;

        var account = await riotApi.AccountV1().GetByRiotIdAsync(platform.ToRegional(), gameName, tagLine, ct);
        return account.Puuid;
    }

    private static LiveGameResponseDto BuildOfflineResponse(string platformRegion)
    {
        return new LiveGameResponseDto(
            State: "offline",
            PlatformRegion: platformRegion,
            GameId: null,
            QueueType: null,
            Map: null,
            GameStartTimeUtc: null,
            GameLengthSeconds: null,
            Participants: [],
            LastUpdatedUtc: DateTime.UtcNow,
            DataAgeSeconds: 0
        );
    }

    private static bool TryParsePlatformRoute(string input, out PlatformRoute platform)
    {
        var normalized = input
            .Replace(" ", string.Empty)
            .Replace("-", string.Empty)
            .Replace("_", string.Empty)
            .ToUpperInvariant();

        if (Enum.TryParse(normalized, true, out platform))
            return true;

        platform = normalized switch
        {
            "NA" => PlatformRoute.NA1,
            "EUW" => PlatformRoute.EUW1,
            "EUNE" => PlatformRoute.EUN1,
            "KR" => PlatformRoute.KR,
            "BR" => PlatformRoute.BR1,
            "LAN" => PlatformRoute.LA1,
            "LAS" => PlatformRoute.LA2,
            "OCE" => PlatformRoute.OC1,
            "JP" => PlatformRoute.JP1,
            "TR" => PlatformRoute.TR1,
            _ => default
        };

        return platform != default;
    }
}
