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

    public async Task<Summoner?> FindByRiotIdAsync(
        string platformRegion,
        string gameName,
        string tagLine,
        Func<IQueryable<Summoner>, IQueryable<Summoner>>? includes = null,
        CancellationToken cancellationToken = default)
    {
        IQueryable<Summoner> query = context.Summoners;
        if (includes != null)
        {
            query = includes(query);
        }
        return await query.FirstOrDefaultAsync(x =>
            x.PlatformRegion == platformRegion && x.GameName == gameName && x.TagLine == tagLine,
            cancellationToken);
    }

    public async Task AddOrUpdateSummonerAsync(Summoner summoner, CancellationToken cancellationToken)
    {
        var existingSummoner =
            await GetSummonerByPuuidAsync(summoner.Puuid!, query => query.Include(s => s.Ranks), cancellationToken);
        if (existingSummoner == null)
        {
            // New summoner: attach with current ranks; EF will insert both
            context.Summoners.Add(summoner);
        }
        else
        {
            // Update scalar properties
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

            // Upsert current ranks and snapshot history if changed, using freshly fetched ranks
            await rankRepository.AddOrUpdateRank(existingSummoner, summoner.Ranks.ToList(), cancellationToken);
        }
    }
}