using Camille.Enums;
using Camille.RiotGames;
using Microsoft.EntityFrameworkCore;
using ProjectSyndraBackend.Data.Models.Account;
using ProjectSyndraBackend.Service.Services.RiotApi.Interfaces;

namespace ProjectSyndraBackend.Service.Services.RiotApi.Implementations;

// SummonerService.cs
using ProjectSyndraBackend.Data.Models.Account;
using ProjectSyndraBackend.Data;

public class SummonerService(RiotGamesApi riotApi, IRankService rankService, ProjectSyndraContext context) : ISummonerService
{
    public async Task<Summoner> GetSummonerByIdAsync(string summonerId, PlatformRoute platformRoute, CancellationToken cancellationToken = default)
    {
        var summoner = await riotApi.SummonerV4().GetBySummonerIdAsync(platformRoute, summonerId, cancellationToken);
        return await CreateSummonerAsync(summoner, platformRoute, cancellationToken);
    }

    public async Task<Summoner> GetSummonerByPuuidAsync(string puuid, PlatformRoute platformRoute, CancellationToken cancellationToken = default)
    {
        var summoner = await riotApi.SummonerV4().GetByPUUIDAsync(platformRoute, puuid, cancellationToken);
        return await CreateSummonerAsync(summoner, platformRoute, cancellationToken);
    }

    private async Task<Summoner> CreateSummonerAsync(Camille.RiotGames.SummonerV4.Summoner summoner, PlatformRoute platformRoute, CancellationToken cancellationToken)
    {
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

        var account = await riotApi.AccountV1().GetByPuuidAsync(RegionalRoute.AMERICAS, summoner.Puuid, cancellationToken);
        current.GameName = account.GameName;
        current.TagLine = account.TagLine;
        current.SummonerName = account.GameName + "#" + account.TagLine;

        var ranks = await rankService.GetRankedDataAsync(current.RiotSummonerId, platformRoute, cancellationToken);

        // Move old ranks to historical table
        var oldRanks = await context.Ranks.Where(r => r.SummonerId == current.RiotSummonerId).ToListAsync(cancellationToken);
        var historicalRanks = oldRanks.Select(oldRank => new HistoricalRank
        {
            SummonerId = oldRank.SummonerId,
            QueueType = oldRank.QueueType,
            Tier = oldRank.Tier,
            RankNumber = oldRank.RankNumber,
            LeaguePoints = oldRank.LeaguePoints,
            Wins = oldRank.Wins,
            Losses = oldRank.Losses,
            DateRecorded = DateTime.UtcNow,
            Summoner = current
        }).ToList();

        context.HistoricalRanks.AddRange(historicalRanks);
        context.Ranks.RemoveRange(oldRanks);

        foreach (var rank in ranks)
            rank.Summoner = current;
        current.Ranks = ranks;

        await context.SaveChangesAsync(cancellationToken);

        return current;
    }
}