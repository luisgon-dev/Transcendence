// SummonerRepository.cs

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Transcendence.Data.Models.LoL.Account;
using Transcendence.Data.Repositories.Interfaces;

namespace Transcendence.Data.Repositories.Implementations;

public class SummonerRepository(TranscendenceContext context, IRankRepository rankRepository) : ISummonerRepository
{
    public async Task<Summoner?> GetSummonerByPuuidAsync(string puuid,
        Func<IQueryable<Summoner>, IQueryable<Summoner>>? includes = null,
        CancellationToken cancellationToken = default)
    {
        IQueryable<Summoner> query = context.Summoners;

        if (includes != null)
        {
            query = includes(query);
        }

        return await query.FirstOrDefaultAsync(x => x.Puuid == puuid, cancellationToken);
    }

    public async Task AddOrUpdateSummonerAsync(Summoner summoner, CancellationToken cancellationToken)
    {
        var existingSummoner =
            await GetSummonerByPuuidAsync(summoner.Puuid!, query => query.Include(s => s.Ranks), cancellationToken);
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
            existingSummoner.RiotSummonerId = summoner.RiotSummonerId;

            await rankRepository.AddOrUpdateRank(existingSummoner.Ranks.ToList(), cancellationToken);
        }

       
    }
    
}