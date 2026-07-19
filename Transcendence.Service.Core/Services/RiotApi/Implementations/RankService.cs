using System.Text.Json;
using Camille.Enums;
using Camille.RiotGames;
using Microsoft.Extensions.Logging;
using Transcendence.Data.Models.LoL.Account;
using Transcendence.Service.Core.Services.RiotApi;
using Transcendence.Service.Core.Services.RiotApi.Interfaces;

namespace Transcendence.Service.Core.Services.RiotApi.Implementations;

public class RankService(
    LeagueRiotApiContext riotApiContext,
    IRankFallbackClient fallbackClient,
    ILogger<RankService> logger) : IRankService
{
    public async Task<List<Rank>> GetRankedDataAsync(string summonerPuuid, PlatformRoute platformRoute,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var entries = await riotApiContext.Api.LeagueV4()
                .GetLeagueEntriesByPUUIDAsync(platformRoute, summonerPuuid, cancellationToken);
            // Normalize and return Rank models without binding to a Summoner; caller will attach
            return entries.Select(e => new Rank
            {
                QueueType = e.QueueType.ToString(),
                Tier = e.Tier.ToString() ?? string.Empty,
                RankNumber = e.Rank.ToString() ?? string.Empty,
                LeaguePoints = e.LeaguePoints,
                Wins = e.Wins,
                Losses = e.Losses
            }).ToList();
        }
        catch (JsonException ex)
        {
            // Riot returned a queueType Camille's QueueType enum doesn't model (Camille is already on its
            // latest nightly), so its typed deserialization throws for the whole array. Re-fetch tolerantly
            // (queueType as a string) so the account keeps its Solo/Flex rank instead of being dropped —
            // and one such account no longer fails the entire high-elo batch.
            logger.LogWarning(ex,
                "Camille could not deserialize league entries for {Puuid} on {Platform} (unmodelled queueType); using tolerant fallback.",
                summonerPuuid, platformRoute);
            return await fallbackClient.GetLeagueEntriesTolerantAsync(summonerPuuid, platformRoute, cancellationToken);
        }
    }
}
