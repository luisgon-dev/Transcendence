using Camille.Enums;
using Camille.RiotGames;
using Camille.RiotGames.MatchV5;
using Microsoft.EntityFrameworkCore;
using Transcendence.Data;
using Transcendence.Data.Models.LoL.Match;
using Transcendence.Data.Repositories.Interfaces;
using Transcendence.Service.Services.RiotApi.Interfaces;
using Transcendence.Service.Services.StaticData.Interfaces;

namespace Transcendence.Service.Services.RiotApi.Implementations;

using DataMatch = Data.Models.LoL.Match.Match;

public class MatchService(
    RiotGamesApi riotGamesApi,
    TranscendenceContext context,
    IMatchRepository matchRepository,
    ISummonerService summonerService,
    ISummonerRepository summonerRepository,
    IStaticDataService staticDataService,
    ILogger<MatchService> logger) : IMatchService
{
    public async Task<DataMatch?> GetMatchDetailsAsync(
        string matchId,
        RegionalRoute regionalRoute,
        PlatformRoute platformRoute,
        CancellationToken cancellationToken = default)
    {
        // Fetch match from Riot
        var matchDto = await riotGamesApi.MatchV5()
            .GetMatchAsync(regionalRoute, matchId, cancellationToken);
        if (matchDto == null)
        {
            logger.LogWarning("Riot API returned null for match {MatchId}", matchId);
            return null;
        }

        var info = matchDto.Info;
        var metadata = matchDto.Metadata;

        // Build match entity (do not persist here; caller handles persistence)
        var match = new DataMatch
        {
            MatchId = metadata.MatchId,
            MatchDate = info.GameCreation, // epoch ms
            Duration = (int)info.GameDuration,
            Patch = NormalizePatch(info.GameVersion),
            QueueType = info.QueueId.ToString(),
            EndOfGameResult = info.EndOfGameResult
        };

        // Ensure static data for this match patch exists
        await staticDataService.EnsureStaticDataForPatchAsync(match.Patch, cancellationToken);

        // Ensure Summoners exist, build participants and relationships
        foreach (var p in info.Participants)
        {
            // Attempt to find Summoner by PUUID
            var summoner = await context.Summoners
                .FirstOrDefaultAsync(s => s.Puuid == p.Puuid, cancellationToken);

            if (summoner == null)
            {
                // Fetch and upsert missing summoner via existing service/repository
                summoner = await summonerService.GetSummonerByPuuidAsync(p.Puuid, platformRoute, cancellationToken);
                await summonerRepository.AddOrUpdateSummonerAsync(summoner, cancellationToken);
            }

            // Link summoner to this match (many-to-many)
            if (match.Summoners.All(s => s.Id != summoner.Id))
            {
                match.Summoners.Add(summoner);
            }

            // Create participant
            var participant = new MatchParticipant
            {
                Match = match,
                Summoner = summoner,
                Puuid = p.Puuid,
                TeamId = (int)p.TeamId,
                ChampionId = (int)p.ChampionId,
                TeamPosition = !string.IsNullOrWhiteSpace(p.TeamPosition) ? p.TeamPosition : p.IndividualPosition,
                Win = p.Win,
                Kills = p.Kills,
                Deaths = p.Deaths,
                Assists = p.Assists,
                ChampLevel = p.ChampLevel,
                GoldEarned = p.GoldEarned,
                TotalDamageDealtToChampions = p.TotalDamageDealtToChampions,
                VisionScore = p.VisionScore,
                TotalMinionsKilled = p.TotalMinionsKilled,
                NeutralMinionsKilled = p.NeutralMinionsKilled,
                SummonerSpell1Id = p.Summoner1Id,
                SummonerSpell2Id = p.Summoner2Id
            };

            participant.Runes = CreateMatchParticipantRunes(p.Perks, match.Patch);
            participant.Items = CreateMatchParticipantItems(p, match.Patch);

            match.Participants.Add(participant);
        }

        logger.LogInformation("Prepared match {MatchId} with {Count} participants for persistence.", matchId,
            match.Participants.Count);
        return match;
    }

    private static string NormalizePatch(string? gameVersion)
    {
        if (string.IsNullOrWhiteSpace(gameVersion)) return string.Empty;
        var parts = gameVersion.Split('.');
        if (parts.Length >= 2)
        {
            return $"{parts[0]}.{parts[1]}";
        }
        return gameVersion;
    }

    private ICollection<MatchParticipantRune> CreateMatchParticipantRunes(Perks perks, string patch)
    {
        var participantRunes = new List<MatchParticipantRune>();
        var seenRunes = new HashSet<int>();

        foreach (var style in perks.Styles)
        {
            foreach (var selection in style.Selections)
            {
                var runeId = selection.Perk;
                if (runeId == 0) continue;
                if (seenRunes.Add(runeId))
                {
                    participantRunes.Add(new MatchParticipantRune
                    {
                        RuneId = runeId,
                        PatchVersion = patch
                    });
                }
            }
        }

        return participantRunes;
    }

    private ICollection<MatchParticipantItem> CreateMatchParticipantItems(Participant participant, string patch)
    {
        // Deduplicate by ItemId to satisfy PK (MatchParticipantId, ItemId)
        var uniqueItemIds = new HashSet<int>();
        void TryAdd(int itemId)
        {
            if (itemId != 0)
                uniqueItemIds.Add(itemId);
        }

        TryAdd(participant.Item0);
        TryAdd(participant.Item1);
        TryAdd(participant.Item2);
        TryAdd(participant.Item3);
        TryAdd(participant.Item4);
        TryAdd(participant.Item5);
        TryAdd(participant.Item6);

        var participantItems = uniqueItemIds
            .Select(id => new MatchParticipantItem { ItemId = id, PatchVersion = patch })
            .ToList();

        return participantItems;
    }


}