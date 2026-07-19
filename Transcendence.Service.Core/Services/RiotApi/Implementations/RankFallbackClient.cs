using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Camille.Enums;
using Microsoft.Extensions.Logging;
using Transcendence.Data.Models.LoL.Account;
using Transcendence.Service.Core.Services.RiotApi.Interfaces;

namespace Transcendence.Service.Core.Services.RiotApi.Implementations;

/// <summary>
/// Raw, enum-free League-V4 entries fetch used only when Camille's typed deserialization throws on an
/// unmodelled <c>queueType</c>. Routes through the same per-region <see cref="IRiotRateGate"/> as the
/// other bespoke Riot clients so the (rare) fallback still respects the key's budget; the <c>X-Riot-Token</c>
/// header is bound on the typed <see cref="HttpClient"/> in DI. Platform endpoints live on the
/// per-platform host, e.g. <c>https://na1.api.riotgames.com</c>.
/// </summary>
public sealed class RankFallbackClient(
    HttpClient httpClient,
    IRiotRateGate rateGate,
    ILogger<RankFallbackClient> logger) : IRankFallbackClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<List<Rank>> GetLeagueEntriesTolerantAsync(
        string summonerPuuid, PlatformRoute platformRoute, CancellationToken cancellationToken = default)
    {
        // Pace under the per-region budget; an exhausted gate degrades to "no rank this cycle" (picked up
        // on the next refresh) rather than risking a 429 on the shared key.
        if (!await rateGate.AcquireAsync(platformRoute.ToString(), cancellationToken))
            return [];

        var host = $"https://{platformRoute.ToString().ToLowerInvariant()}.api.riotgames.com";
        var url = $"{host}/lol/league/v4/entries/by-puuid/{Uri.EscapeDataString(summonerPuuid)}";

        using var response = await httpClient.GetAsync(url, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return []; // account has no ranked entries

        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning(
                "Tolerant league-entries fallback returned {Status} for {Puuid} on {Platform}.",
                (int)response.StatusCode, summonerPuuid, platformRoute);
            return [];
        }

        var entries = await response.Content
            .ReadFromJsonAsync<List<LeagueEntryRaw>>(JsonOptions, cancellationToken) ?? [];

        return entries.Select(e => new Rank
        {
            QueueType = e.QueueType ?? string.Empty,
            Tier = e.Tier ?? string.Empty,
            RankNumber = e.Rank ?? string.Empty,
            LeaguePoints = e.LeaguePoints,
            Wins = e.Wins,
            Losses = e.Losses
        }).ToList();
    }

    // Mirrors the shape RankService needs; queueType/tier/rank stay strings so no value can fail to bind.
    private sealed record LeagueEntryRaw(
        string? QueueType, string? Tier, string? Rank, int LeaguePoints, int Wins, int Losses);
}
