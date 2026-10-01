using System.Net;
using System.Text.Json;
using Camille.Enums;
using Camille.RiotGames.Util;
using Microsoft.Extensions.Options;
using Transcendence.Service.Core.Services.RiotApi.Interfaces;

namespace Transcendence.Service.Core.Services.RiotApi.Implementations;

public sealed class RiotSpectatorOptions
{
    public string ApiKey { get; set; } = "";
    public string ApiUrlTemplate { get; set; } = "https://{0}.api.riotgames.com";
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(15);
}

public sealed record RiotSpectatorGame(long GameId, long GameStartTime, long GameLength,
    int GameQueueConfigId, int MapId, IReadOnlyList<RiotSpectatorParticipant> Participants);
public sealed record RiotSpectatorParticipant(string? Puuid, string? RiotId, string? SummonerId,
    int TeamId, int ChampionId, int Spell1Id, int Spell2Id, int ProfileIconId, RiotSpectatorPerks? Perks);
public sealed record RiotSpectatorPerks(IReadOnlyList<int> PerkIds, int PerkStyle, int PerkSubStyle);

/// <summary>
/// Status-preserving Spectator boundary: Camille collapses HTTP 404, 204 and 422
/// to the same null value. Only a real 404 verifies that a stored player is offline.
/// Worker rate gating and durable Hangfire retries surround this finite HTTP request.
/// </summary>
public sealed class RiotSpectatorClient(HttpClient client, IOptions<RiotSpectatorOptions> options) : IRiotSpectatorClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<RiotSpectatorGame?> GetCurrentGameAsync(PlatformRoute platform, string puuid, CancellationToken ct = default)
    {
        var settings = options.Value;
        using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        requestTimeout.CancelAfter(settings.RequestTimeout);
        var requestToken = requestTimeout.Token;
        var host = string.Format(settings.ApiUrlTemplate, platform.ToString().ToLowerInvariant()).TrimEnd('/');
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"{host}/lol/spectator/v5/active-games/by-summoner/{Uri.EscapeDataString(puuid)}");
        request.Headers.Add("X-Riot-Token", settings.ApiKey);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, requestToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        if (response.StatusCode != HttpStatusCode.OK)
            throw new RiotResponseException("Spectator returned an unverified response.", response);

        var game = await JsonSerializer.DeserializeAsync<RiotSpectatorGame>(
            await response.Content.ReadAsStreamAsync(requestToken), JsonOptions, requestToken);
        if (game is null || game.GameId <= 0 || game.GameStartTime <= 0 || game.GameLength < 0 || game.MapId <= 0 ||
            game.Participants is not { Count: > 0 } ||
            game.Participants.Any(x => x is null || x.ChampionId <= 0 || x.TeamId is not (100 or 200)))
            throw new JsonException("Spectator returned an incomplete active-game payload.");
        return game;
    }
}
