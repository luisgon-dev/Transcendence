using Camille.Enums;
using Camille.RiotGames;
using Transcendence.Data.Models.LoL.Account;
using Transcendence.Service.Core.Services.RiotApi;
using Transcendence.Service.Core.Services.RiotApi.Interfaces;

namespace Transcendence.Service.Core.Services.RiotApi.Implementations;

// SummonerService.cs
public class SummonerService(LeagueRiotApiContext riotApiContext, IRankService rankService)
    : ISummonerService
{
    public async Task<Summoner> GetSummonerByPuuidAsync(string puuid, PlatformRoute platformRoute,
        CancellationToken cancellationToken = default)
    {
        var summoner = await riotApiContext.Api.SummonerV4().GetByPUUIDAsync(platformRoute, puuid, cancellationToken);
        return await CreateSummonerAsync(summoner, platformRoute, cancellationToken);
    }

    public async Task<Summoner> GetSummonerByRiotIdAsync(string gameName, string tagLine, PlatformRoute platformRoute,
        CancellationToken cancellationToken = default)
    {
        var regional = platformRoute.ToAccountRegional();
        var account = await riotApiContext.Api.AccountV1()
            .GetByRiotIdAsync(regional, gameName, tagLine, cancellationToken);
        if (account == null || string.IsNullOrWhiteSpace(account.Puuid))
            throw new RiotAccountNotFoundException(
                $"Riot Account API returned no PUUID for {gameName}#{tagLine} on {regional}.");

        var summoner = await riotApiContext.Api.SummonerV4()
            .GetByPUUIDAsync(platformRoute, account.Puuid, cancellationToken);

        // Reuse the account we already resolved above instead of re-fetching it (saves 1 Account-V1 call).
        return await CreateSummonerAsync(summoner, platformRoute, cancellationToken, account);
    }

    private async Task<Summoner> CreateSummonerAsync(Camille.RiotGames.SummonerV4.Summoner summoner,
        PlatformRoute platformRoute, CancellationToken cancellationToken,
        Camille.RiotGames.AccountV1.Account? account = null)
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

        account ??= await riotApiContext.Api.AccountV1()
            .GetByPuuidAsync(platformRoute.ToAccountRegional(), summoner.Puuid, cancellationToken);
        if (account == null)
            throw new InvalidOperationException(
                $"Riot Account API returned no account for PUUID {summoner.Puuid} on {platformRoute.ToAccountRegional()}.");

        current.GameName = account.GameName;
        current.TagLine = account.TagLine;
        current.SummonerName = $"{account.GameName}#{account.TagLine}";

        var latestRank = await rankService.GetRankedDataAsync(current.Puuid, platformRoute, cancellationToken);

        if (latestRank.Count > 0)
            current.Ranks = latestRank;
        else
            current.Ranks = [];
        return current;
    }
}
