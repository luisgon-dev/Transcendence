using Camille.Enums;
using Camille.RiotGames;
using Camille.RiotGames.Util;
using Microsoft.Extensions.Caching.Hybrid;
using System.Text.Json;
using Transcendence.Data.Repositories.Interfaces;
using Transcendence.Service.Core.Services.LiveGame.Interfaces;
using Transcendence.Service.Core.Services.LiveGame.Models;
using Transcendence.Service.Core.Services.RiotApi;

namespace Transcendence.Service.Core.Services.LiveGame.Implementations;

public class RiotLiveGamePollingService(
    LeagueRiotApiContext riotApiContext,
    ISummonerRepository summonerRepository,
    IRiotRateGate rateGate,
    HybridCache cache,
    ILiveGameAnalysisService liveGameAnalysisService,
    ILogger<RiotLiveGamePollingService> logger) : ILiveGamePollingService
{
    private static readonly HybridCacheEntryOptions LiveGameCacheOptions = new()
    {
        Expiration = TimeSpan.FromMinutes(2),
        LocalCacheExpiration = TimeSpan.FromSeconds(30)
    };

    public async Task<LiveGameResponseDto> FetchCurrentGameAsync(
        string platformRegion,
        string gameName,
        string tagLine,
        CancellationToken ct = default)
    {
        var request = NormalizeRequest(platformRegion, gameName, tagLine);

        return await cache.GetOrCreateAsync(
            request.CacheKey,
            async cancel => await FetchFreshAsync(request, cancel),
            LiveGameCacheOptions,
            tags: ["live-game"],
            cancellationToken: ct
        );
    }

    public async Task<LiveGameResponseDto> ProbeCurrentGameAsync(
        string platformRegion,
        string gameName,
        string tagLine,
        CancellationToken ct = default)
    {
        var request = NormalizeRequest(platformRegion, gameName, tagLine);
        var response = await FetchFreshAsync(request, ct);
        await cache.SetAsync(
            request.CacheKey,
            response,
            LiveGameCacheOptions,
            tags: ["live-game"],
            cancellationToken: ct);
        return response;
    }

    private async Task<LiveGameResponseDto> FetchFreshAsync(NormalizedLiveGameRequest request, CancellationToken ct)
    {
        var puuid = await ResolvePuuidAsync(request.Platform, request.GameName, request.TagLine, ct);
        if (string.IsNullOrWhiteSpace(puuid))
            return BuildOfflineResponse(request.Region);

        try
        {
            if (!await rateGate.AcquireAsync(request.Region, ct))
                throw new InvalidOperationException($"Live-game probe deferred by the {request.Region} Riot rate gate.");

            var gameInfo = await riotApiContext.Api.SpectatorV5()
                .GetCurrentGameInfoByPuuidAsync(request.Platform, puuid, ct);
            if (gameInfo == null)
                return BuildOfflineResponse(request.Region);

            var participants = gameInfo.Participants?
                .Where(p => p is not null)
                .Select(p => new LiveGameParticipantDto(
                    Puuid: p!.Puuid ?? string.Empty,
                    RiotId: p.RiotId,
                    SummonerId: p.SummonerId,
                    TeamId: (int)p.TeamId,
                    ChampionId: (int)p.ChampionId,
                    Spell1Id: (int)p.Spell1Id,
                    Spell2Id: (int)p.Spell2Id,
                    ProfileIconId: (int)p.ProfileIconId,
                    PerkIds: p.Perks?.PerkIds?.Select(id => (int)id).ToList() ?? [],
                    PerkStyleId: p.Perks is null ? null : (int)p.Perks.PerkStyle,
                    PerkSubStyleId: p.Perks is null ? null : (int)p.Perks.PerkSubStyle
                )).ToList() ?? [];

            var response = new LiveGameResponseDto(
                State: "in_game",
                PlatformRegion: request.Region,
                GameId: gameInfo.GameId.ToString(),
                QueueType: gameInfo.GameQueueConfigId?.ToString(),
                Map: gameInfo.MapId.ToString(),
                GameStartTimeUtc: DateTimeOffset.FromUnixTimeMilliseconds(gameInfo.GameStartTime).UtcDateTime,
                GameLengthSeconds: gameInfo.GameLength,
                Participants: participants,
                LastUpdatedUtc: DateTime.UtcNow,
                DataAgeSeconds: 0);

            var analysis = await liveGameAnalysisService.AnalyzeAsync(request.Region, response, ct);
            return response with { Analysis = analysis };
        }
        catch (RiotResponseException ex) when (ex.GetResponse()?.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return BuildOfflineResponse(request.Region);
        }
        catch (Exception ex) when (RiotRateLimitHandling.TryGetRetryAfter(ex, out var retryAfter))
        {
            rateGate.Pause(request.Region, retryAfter);
            throw;
        }
        catch (JsonException ex)
        {
            logger.LogInformation(
                "Spectator payload parse fallback for {Region}/{GameName}#{TagLine}; treating as offline. Error: {Error}",
                request.Region,
                request.GameName,
                request.TagLine,
                ex.Message);
            return BuildOfflineResponse(request.Region);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Failed to fetch live game for {Region}/{GameName}#{TagLine}",
                request.Region,
                request.GameName,
                request.TagLine);
            throw;
        }
    }

    private static NormalizedLiveGameRequest NormalizeRequest(
        string platformRegion,
        string gameName,
        string tagLine)
    {
        if (!PlatformRouteParser.TryParse(platformRegion, out var platform))
            throw new ArgumentException($"Unsupported platform region '{platformRegion}'.", nameof(platformRegion));

        var normalizedRegion = platform.ToString();
        var normalizedGameName = gameName.Trim();
        var normalizedTagLine = tagLine.Trim();
        return new NormalizedLiveGameRequest(
            platform,
            normalizedRegion,
            normalizedGameName,
            normalizedTagLine,
            $"livegame:{normalizedRegion}:{normalizedGameName.ToUpperInvariant()}:{normalizedTagLine.ToUpperInvariant()}");
    }

    private sealed record NormalizedLiveGameRequest(
        PlatformRoute Platform,
        string Region,
        string GameName,
        string TagLine,
        string CacheKey);

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

        try
        {
            var account = await riotApiContext.Api.AccountV1().GetByRiotIdAsync(platform.ToAccountRegional(), gameName, tagLine, ct);
            return account?.Puuid;
        }
        catch (RiotResponseException ex) when (ex.GetResponse()?.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
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

}
