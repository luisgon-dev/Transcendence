using Camille.Enums;
using Camille.RiotGames;
using Transcendence.Data.Models.LoL.Account;
using Transcendence.Service.Core.RiotApi.Interfaces;
namespace Transcendence.Service.Core.RiotApi.Implementations;

// SummonerService.cs
public class SummonerService(RiotGamesApi riotApi, IRankService rankService)
    : ISummonerService
{
    public async Task<Summoner> GetSummonerByPuuidAsync(string puuid, PlatformRoute platformRoute,
        CancellationToken cancellationToken = default)
    {
        var summoner = await riotApi.SummonerV4().GetByPUUIDAsync(platformRoute, puuid, cancellationToken);
        return await CreateSummonerAsync(summoner, platformRoute, cancellationToken);
    }

    public async Task<Summoner> GetSummonerByRiotIdAsync(string gameName, string tagLine, PlatformRoute platformRoute,
        CancellationToken cancellationToken = default)
    {
        var regional = platformRoute.ToRegional();
        var account = await riotApi.AccountV1()
            .GetByRiotIdAsync(regional, gameName, tagLine, cancellationToken);

        var summoner = await riotApi.SummonerV4()
            .GetByPUUIDAsync(platformRoute, account.Puuid, cancellationToken);

        return await CreateSummonerAsync(summoner, platformRoute, cancellationToken);
    }

    async Task<Summoner> CreateSummonerAsync(Camille.RiotGames.SummonerV4.Summoner summoner,
        PlatformRoute platformRoute, CancellationToken cancellationToken)
    {
        var current = new Summoner
        {
            RiotSummonerId = summoner.Id,
            Puuid = summoner.Puuid,
            AccountId = summoner.AccountId,
            ProfileIconId = summoner.ProfileIconId,
            RevisionDate = summoner.RevisionDate,
            SummonerLevel = summoner.SummonerLevel,
            PlatformRegion = platformRoute.ToString(),
            Region = platformRoute.ToRegional().ToString()
        };

        var account = await riotApi.AccountV1()
            .GetByPuuidAsync(platformRoute.ToRegional(), summoner.Puuid, cancellationToken);
        current.GameName = account.GameName;
        current.TagLine = account.TagLine;
        current.SummonerName = account.GameName + "#" + account.TagLine;

        var latestRank = await rankService.GetRankedDataAsync(current.Puuid, platformRoute, cancellationToken);

        if (latestRank.Count > 0)
        {
            current.Ranks = latestRank;
        }
        else
        {
            current.Ranks = [];
        }
        return current;
    }
}
