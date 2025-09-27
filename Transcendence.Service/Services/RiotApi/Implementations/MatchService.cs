using Camille.Enums;
using Camille.RiotGames;
using Camille.RiotGames.MatchV5;
using Microsoft.EntityFrameworkCore;
using Transcendence.Data;
using Transcendence.Data.Models.LoL.Match;
using Transcendence.Data.Repositories.Interfaces;
using Transcendence.Service.Services.RiotApi.Interfaces;

namespace Transcendence.Service.Services.RiotApi.Implementations;

using DataMatch = Data.Models.LoL.Match.Match;

public class MatchService(
    RiotGamesApi riotGamesApi,
    TranscendenceContext context,
    IMatchRepository matchRepository,
    ISummonerService summonerService,
    ISummonerRepository summonerRepository,
    IRuneRepository runeRepository,
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
            Patch = info.GameVersion,
            QueueType = info.QueueId.ToString(),
            EndOfGameResult = info.EndOfGameResult
        };

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
                SummonerSpell2Id = p.Summoner2Id,
                Item0 = p.Item0,
                Item1 = p.Item1,
                Item2 = p.Item2,
                Item3 = p.Item3,
                Item4 = p.Item4,
                Item5 = p.Item5,
                Item6 = p.Item6,
                TrinketItem = p.Item6 // trinket slot by convention
            };
            participant.Runes = await GetOrCreateRunesAsync(p.Perks, cancellationToken);
            match.Participants.Add(participant);
        }

        logger.LogInformation("Prepared match {MatchId} with {Count} participants for persistence.", matchId,
            match.Participants.Count);
        return match;
    }

    private async Task<Runes> GetOrCreateRunesAsync(
        Perks perks,
        CancellationToken cancellationToken)
    {
        int primaryStyle = 0, subStyle = 0;
        var primaryRunes = new int[4];
        var subRunes = new int[2];
        var primaryRuneVars = new int[4][];
        var subRuneVars = new int[2][];

        foreach (var style in perks.Styles)
        {
            if (style.Description == "primaryStyle")
            {
                primaryStyle = style.Style;
                for (int i = 0; i < 4; i++)
                {
                    primaryRunes[i] = style.Selections[i].Perk;
                    primaryRuneVars[i] = new[]
                    {
                        style.Selections[i].Var1,
                        style.Selections[i].Var2,
                        style.Selections[i].Var3
                    };
                }
            }
            else if (style.Description == "subStyle")
            {
                subStyle = style.Style;
                for (int i = 0; i < 2; i++)
                {
                    subRunes[i] = style.Selections[i].Perk;
                    subRuneVars[i] = new[]
                    {
                        style.Selections[i].Var1,
                        style.Selections[i].Var2,
                        style.Selections[i].Var3
                    };
                }
            }
        }

        var existingRunes = await runeRepository.GetExistingRunesAsync(
            primaryStyle, subStyle,
            primaryRunes, subRunes,
            perks.StatPerks.Defense,
            perks.StatPerks.Flex,
            perks.StatPerks.Offense,
            cancellationToken);

        if (existingRunes != null)
            return existingRunes;

        var newRunes = new Runes
        {
            PrimaryStyle = primaryStyle,
            SubStyle = subStyle,
            Perk0 = primaryRunes[0],
            Perk1 = primaryRunes[1],
            Perk2 = primaryRunes[2],
            Perk3 = primaryRunes[3],
            Perk4 = subRunes[0],
            Perk5 = subRunes[1],
            StatDefense = perks.StatPerks.Defense,
            StatFlex = perks.StatPerks.Flex,
            StatOffense = perks.StatPerks.Offense
        };

        // Set rune vars
        for (int i = 0; i < 3; i++)
        {
            newRunes.RuneVars0[i] = primaryRuneVars[0][i];
            newRunes.RuneVars1[i] = primaryRuneVars[1][i];
            newRunes.RuneVars2[i] = primaryRuneVars[2][i];
            newRunes.RuneVars3[i] = primaryRuneVars[3][i];
            newRunes.RuneVars4[i] = subRuneVars[0][i];
            newRunes.RuneVars5[i] = subRuneVars[1][i];
        }

        return await runeRepository.AddRunesAsync(newRunes, cancellationToken);
    }
}