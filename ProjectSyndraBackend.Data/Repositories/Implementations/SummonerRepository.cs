// SummonerRepository.cs

using Microsoft.EntityFrameworkCore;
using ProjectSyndraBackend.Data.Models.Account;

namespace ProjectSyndraBackend.Data.Repositories.Implementations;

public class SummonerRepository(ProjectSyndraContext context) : ISummonerRepository
{
    public async Task<Summoner?> GetSummonerByIdAsync(string summonerId, string platformRoute,
        CancellationToken cancellationToken)
    {
        return await context.Summoners.FirstOrDefaultAsync(
            x => x.SummonerId == summonerId && x.PlatformRegion == platformRoute, cancellationToken);
    }

    public async Task<Summoner?> GetSummonerByPuuidAsync(string puuid, CancellationToken cancellationToken)
    {
        return await context.Summoners.FirstOrDefaultAsync(x => x.Puuid == puuid, cancellationToken);
    }

    public async Task AddOrUpdateSummonerAsync(Summoner summoner, CancellationToken cancellationToken)
    {
        var existingSummoner =
            await GetSummonerByIdAsync(summoner.SummonerId!, summoner.PlatformRegion!, cancellationToken);
        if (existingSummoner == null)
        {
            context.Summoners.Add(summoner);
        }
        else
        {
            existingSummoner.Puuid = summoner.Puuid;
            existingSummoner.AccountId = summoner.AccountId;
            existingSummoner.ProfileIconId = summoner.ProfileIconId;
            existingSummoner.RevisionDate = summoner.RevisionDate;
            existingSummoner.SummonerLevel = summoner.SummonerLevel;
            existingSummoner.GameName = summoner.GameName;
            existingSummoner.TagLine = summoner.TagLine;
            existingSummoner.SummonerName = summoner.SummonerName;
            existingSummoner.PlatformRegion = summoner.PlatformRegion;
            existingSummoner.Region = summoner.Region;
        }

        await context.SaveChangesAsync(cancellationToken);
    }
}