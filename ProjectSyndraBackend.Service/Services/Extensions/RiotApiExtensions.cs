using Camille.Enums;
using Camille.RiotGames;
using ProjectSyndraBackend.Data.Models.Account;

namespace ProjectSyndraBackend.Service.Services.Extensions;

public static class RiotApiExtensions
{
    public static async Task<Summoner> GetSummoner(this RiotGamesApi riotApi, string summonerId,
        PlatformRoute platformRoute, CancellationToken cancellationToken = default)
    {
        var summoner = await riotApi.SummonerV4().GetBySummonerIdAsync(platformRoute, summonerId, cancellationToken);
        var current = new Summoner
        {
            SummonerId = summoner.Puuid,
            RiotSummonerId = summoner.Id,
            Puuid = summoner.Puuid,
            AccountId = summoner.AccountId,
            ProfileIconId = summoner.ProfileIconId,
            RevisionDate = summoner.RevisionDate,
            SummonerLevel = summoner.SummonerLevel
        };

        // fetch the account from the puuid, can use any region here.
        var account = await riotApi.AccountV1()
            .GetByPuuidAsync(RegionalRoute.AMERICAS, summoner.Puuid, cancellationToken);

        current.GameName = account.GameName;
        current.TagLine = account.TagLine;
        current.SummonerName = account.GameName + "#" + account.TagLine;

        return current;
    }
}