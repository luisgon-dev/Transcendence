using Camille.Enums;
using Camille.RiotGames;
using ProjectSyndraBackend.Data.Models.LoL.Account;
using ProjectSyndraBackend.Data.Models.LoL.Match;
using ProjectSyndraBackend.Data.Repositories;
using ProjectSyndraBackend.Service.Services.RiotApi.Interfaces;

namespace ProjectSyndraBackend.Service.Services.RiotApi.Implementations;

public class MatchService(
    ISummonerService summonerService,
    ISummonerRepository summonerRepository,
    RiotGamesApi riotGamesApi) : IMatchService
{
    public async Task<Match?> GetMatchDetailsAsync(string matchId, RegionalRoute regionalRoute,
        PlatformRoute platformRoute, CancellationToken cancellationToken = default)
    {
        var details = await riotGamesApi.MatchV5().GetMatchAsync(regionalRoute, matchId, cancellationToken);

        if (details == null) return null;
        var match = new Match
        {
            ExternalMatchId = details.Metadata.MatchId, // Use ExternalMatchId instead of MatchId
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
            var summoner = await summonerRepository.GetSummonerByPuuidAsync(participant.Puuid, cancellationToken);

            if (summoner == null)
            {
                summoner = await summonerService.GetSummonerByPuuidAsync(participant.Puuid, platformRoute,
                    cancellationToken);
                await summonerRepository.AddOrUpdateSummonerAsync(summoner, cancellationToken);
            }

            match.MatchSummoners.Add(new MatchSummoner
            {
                MatchId = match.Id, // Use GUID primary key
                Match = match,
                SummonerId = summoner.Id, // Use GUID primary key
                Summoner = summoner,
                ExternalMatchId = match.ExternalMatchId, // Store external IDs for reference
                ExternalSummonerId = summoner.ExternalSummonerId
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
                MatchId = match.Id, // Use GUID primary key
                SummonerId = summoner!.Id, // Use GUID primary key
                Summoner = summoner,
                ExternalMatchId = match.ExternalMatchId, // Store external IDs for reference
                ExternalSummonerId = summoner.ExternalSummonerId
            };

            items.Add(info.Item0);
            items.Add(info.Item1);
            items.Add(info.Item2);
            items.Add(info.Item3);
            items.Add(info.Item4);
            items.Add(info.Item5);
            items.Add(info.Item6);

            matchDetail.Items = items;

            var localRunes = new Runes();

            foreach (var rune in info.Perks.Styles)
                switch (rune.Description)
                {
                    case "primaryStyle":
                        localRunes.PrimaryStyle = rune.Style;

                        localRunes.Perk0 = rune.Selections[0].Perk;
                        localRunes.Perk1 = rune.Selections[1].Perk;
                        localRunes.Perk2 = rune.Selections[2].Perk;
                        localRunes.Perk3 = rune.Selections[3].Perk;

                        // for each selection append all var1, var2, var3 to the rune vars.
                        localRunes.RuneVars0[0] = rune.Selections[0].Var1;
                        localRunes.RuneVars1[0] = rune.Selections[1].Var1;
                        localRunes.RuneVars2[0] = rune.Selections[2].Var1;
                        localRunes.RuneVars3[0] = rune.Selections[3].Var1;

                        localRunes.RuneVars0[1] = rune.Selections[0].Var2;
                        localRunes.RuneVars1[1] = rune.Selections[1].Var2;
                        localRunes.RuneVars2[1] = rune.Selections[2].Var2;
                        localRunes.RuneVars3[1] = rune.Selections[3].Var2;

                        localRunes.RuneVars0[2] = rune.Selections[0].Var3;
                        localRunes.RuneVars1[2] = rune.Selections[1].Var3;
                        localRunes.RuneVars2[2] = rune.Selections[2].Var3;
                        localRunes.RuneVars3[2] = rune.Selections[3].Var3;
                        break;
                    case "subStyle":
                        localRunes.SubStyle = rune.Style;

                        localRunes.Perk4 = rune.Selections[0].Perk;
                        localRunes.Perk5 = rune.Selections[1].Perk;

                        // for each selection append all var1, var2, var3 to the rune vars.
                        localRunes.RuneVars4[0] = rune.Selections[0].Var1;
                        localRunes.RuneVars5[0] = rune.Selections[1].Var1;

                        localRunes.RuneVars4[1] = rune.Selections[0].Var2;
                        localRunes.RuneVars5[1] = rune.Selections[1].Var2;

                        localRunes.RuneVars4[2] = rune.Selections[0].Var3;
                        localRunes.RuneVars5[2] = rune.Selections[1].Var3;
                        break;
                }

            localRunes.StatDefense = info.Perks.StatPerks.Defense;
            localRunes.StatFlex = info.Perks.StatPerks.Flex;
            localRunes.StatOffense = info.Perks.StatPerks.Offense;

            matchDetail.Runes = localRunes;

            localMatchDetails.Add(matchDetail);
        }

        match.MatchDetails = localMatchDetails;

        return match;
    }
}
