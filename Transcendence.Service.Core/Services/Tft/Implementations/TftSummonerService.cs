using Camille.Enums;
using Camille.RiotGames;
using Transcendence.Data.Models.Tft.Account;
using Transcendence.Service.Core.Services.RiotApi;
using Transcendence.Service.Core.Services.Tft.Interfaces;

namespace Transcendence.Service.Core.Services.Tft.Implementations;

public class TftSummonerService(TftRiotApiContext riotApiContext, ITftRankService rankService) : ITftSummonerService
{
    public async Task<TftSummoner> GetSummonerByPuuidAsync(string puuid, PlatformRoute platformRoute, CancellationToken ct = default)
    {
        var summoner = await riotApiContext.Api.TftSummonerV1().GetByPUUIDAsync(platformRoute, puuid, ct);
        if (summoner == null || string.IsNullOrWhiteSpace(summoner.Puuid))
            throw new InvalidOperationException($"Riot TFT Summoner API returned no summoner for PUUID {puuid} on {platformRoute}.");

        return await CreateAsync(summoner, platformRoute, ct);
    }

    public async Task<TftSummoner> GetSummonerByRiotIdAsync(string gameName, string tagLine, PlatformRoute platformRoute,
        CancellationToken ct = default)
    {
        var account = await riotApiContext.Api.AccountV1().GetByRiotIdAsync(platformRoute.ToAccountRegional(), gameName, tagLine, ct);
        if (account == null || string.IsNullOrWhiteSpace(account.Puuid))
            throw new RiotAccountNotFoundException($"Riot Account API returned no PUUID for {gameName}#{tagLine}.");

        var summoner = await riotApiContext.Api.TftSummonerV1().GetByPUUIDAsync(platformRoute, account.Puuid, ct);
        if (summoner == null || string.IsNullOrWhiteSpace(summoner.Puuid))
        {
            throw new InvalidOperationException(
                $"Riot TFT Summoner API returned no summoner for {gameName}#{tagLine} on {platformRoute}.");
        }

        return await CreateAsync(summoner, platformRoute, ct);
    }

    private async Task<TftSummoner> CreateAsync(Camille.RiotGames.TftSummonerV1.Summoner summoner, PlatformRoute platformRoute,
        CancellationToken ct)
    {
        var account = await riotApiContext.Api.AccountV1().GetByPuuidAsync(platformRoute.ToAccountRegional(), summoner.Puuid, ct);
        if (account == null || string.IsNullOrWhiteSpace(account.GameName) || string.IsNullOrWhiteSpace(account.TagLine))
            throw new InvalidOperationException($"Unable to resolve Riot account for TFT PUUID {summoner.Puuid}.");

        return new TftSummoner
        {
            RiotSummonerId = summoner.Id,
            ProfileIconId = summoner.ProfileIconId,
            SummonerLevel = summoner.SummonerLevel,
            RevisionDate = summoner.RevisionDate,
            Puuid = summoner.Puuid,
            GameName = account.GameName,
            TagLine = account.TagLine,
            AccountId = summoner.AccountId,
            PlatformRegion = platformRoute.ToString(),
            Region = platformRoute.ToRegional().ToString(),
            Ranks = await rankService.GetRankedDataAsync(summoner.Puuid, platformRoute, ct)
        };
    }
}
