using Camille.Enums;
using Camille.RiotGames;
using Camille.RiotGames.MatchV5;
using Transcendence.Data.Models.LoL.Account;
using Transcendence.Data.Models.LoL.Match;
using Transcendence.Data.Repositories;
using Transcendence.Data.Repositories.Implementations;
using Transcendence.Data.Repositories.Interfaces;
using Transcendence.Service.Services.RiotApi.Interfaces;
using Match = Transcendence.Data.Models.LoL.Match.Match;

namespace Transcendence.Service.Services.RiotApi.Implementations;

public class MatchService(
    ISummonerService summonerService,
    ISummonerRepository summonerRepository,
    IRuneRepository runeRepository,
    RiotGamesApi riotGamesApi) : IMatchService
{
    public async Task<Match?> GetMatchDetailsAsync(string matchId, RegionalRoute regionalRoute,
        PlatformRoute platformRoute, CancellationToken cancellationToken = default)
    {
        var details = await riotGamesApi.MatchV5().GetMatchAsync(regionalRoute, matchId, cancellationToken);

        if (details == null) return null;
        var match = new Match
        {
            MatchId = details.Metadata.MatchId,
            MatchDate = details.Info.GameCreation,
            Duration = (int)details.Info.GameDuration,
            Patch = details.Info.GameVersion,
            QueueType = details.Info.QueueId.ToString(),
            EndOfGameResult = details.Info.EndOfGameResult
        };

        var localSummoners = new List<Summoner>();
        var localMatchDetails = new List<MatchDetail>();

        foreach (var participant in details.Info.Participants)
        {
            var summoner = await summonerRepository.GetSummonerByPuuidAsync(participant.Puuid, null, cancellationToken);

            if (summoner == null)
            {
                summoner = await summonerService.GetSummonerByPuuidAsync(participant.Puuid, platformRoute,
                    cancellationToken);
                await summonerRepository.AddOrUpdateSummonerAsync(summoner, cancellationToken);
            }

            match.MatchSummoners.Add(new MatchSummoner
            {
                Match = match,
                Summoner = summoner
            });
            localSummoners.Add(summoner);
        }


        foreach (var info in details.Info.Participants)
        {
            var summoner = localSummoners.FirstOrDefault(x => x.Puuid == info.Puuid);
            var items = new List<int>();

            var matchDetail = new MatchDetail
            {
                Kills = info.Kills,
                Deaths = info.Deaths,
                Assists = info.Assists,
                Win = info.Win,
                SummonerSpell1 = info.Summoner1Id,
                SummonerSpell2 = info.Summoner2Id,
                Lane = info.Lane,
                Role = info.Role,
                ChampionName = info.ChampionName,
                ChampionId = (int)info.ChampionId,
                Match = match,
                Summoner = summoner!
            };

            items.Add(info.Item0);
            items.Add(info.Item1);
            items.Add(info.Item2);
            items.Add(info.Item3);
            items.Add(info.Item4);
            items.Add(info.Item5);
            items.Add(info.Item6);

            matchDetail.Items = items;

            matchDetail.Runes = await GetOrCreateRunesAsync(info.Perks, cancellationToken);

            localMatchDetails.Add(matchDetail);
        }

        match.MatchDetails = localMatchDetails;
        

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
                    primaryRuneVars[i] = new[] {
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
                    subRuneVars[i] = new[] {
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