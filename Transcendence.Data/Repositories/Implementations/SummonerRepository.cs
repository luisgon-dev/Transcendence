// SummonerRepository.cs

using Microsoft.EntityFrameworkCore;
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

        if (includes != null) query = includes(query);

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
        if (includes != null) query = includes(query);

        var normalizedPlatformRegion = string.IsNullOrWhiteSpace(platformRegion) ? null : platformRegion.Trim();
        var normalizedGameName = NormalizeValue(gameName);
        var normalizedTagLine = NormalizeValue(tagLine);
        var normalizedGameNameKey = NormalizeForLookup(normalizedGameName);
        var normalizedTagLineKey = NormalizeForLookup(normalizedTagLine);

        if (normalizedPlatformRegion == null ||
            normalizedGameName == null ||
            normalizedTagLine == null ||
            normalizedGameNameKey == null ||
            normalizedTagLineKey == null)
            return null;

        // Fast path: normalized match can leverage the composite index.
        var normalizedMatch = await query.FirstOrDefaultAsync(x =>
                x.PlatformRegion == normalizedPlatformRegion &&
                x.GameNameNormalized == normalizedGameNameKey &&
                x.TagLineNormalized == normalizedTagLineKey,
            cancellationToken);

        if (normalizedMatch != null)
            return normalizedMatch;

        // Secondary exact match for rows written before normalization fields were introduced.
        var exactMatch = await query.FirstOrDefaultAsync(x =>
                x.PlatformRegion == normalizedPlatformRegion &&
                x.GameName == normalizedGameName &&
                x.TagLine == normalizedTagLine,
            cancellationToken);

        if (exactMatch != null)
            return exactMatch;

        // Legacy fallback for older rows where normalized fields are not present.
        return await query.FirstOrDefaultAsync(x =>
                x.PlatformRegion == normalizedPlatformRegion &&
                x.GameNameNormalized == null &&
                x.TagLineNormalized == null &&
                x.GameName != null &&
                x.TagLine != null &&
                x.GameName.ToUpper() == normalizedGameNameKey &&
                x.TagLine.ToUpper() == normalizedTagLineKey,
            cancellationToken);
    }

    public async Task AddOrUpdateSummonerAsync(Summoner summoner, CancellationToken cancellationToken)
    {
        summoner.GameName = NormalizeValue(summoner.GameName);
        summoner.TagLine = NormalizeValue(summoner.TagLine);
        summoner.GameNameNormalized = NormalizeForLookup(summoner.GameName);
        summoner.TagLineNormalized = NormalizeForLookup(summoner.TagLine);

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
            existingSummoner.GameNameNormalized = summoner.GameNameNormalized;
            existingSummoner.TagLineNormalized = summoner.TagLineNormalized;
            existingSummoner.SummonerName = summoner.SummonerName;
            existingSummoner.PlatformRegion = summoner.PlatformRegion;
            existingSummoner.Region = summoner.Region;
            existingSummoner.RiotSummonerId = summoner.RiotSummonerId;

            // Upsert current ranks and snapshot history if changed, using freshly fetched ranks
            await rankRepository.AddOrUpdateRank(existingSummoner, summoner.Ranks.ToList(), cancellationToken);
        }
    }

    private static string? NormalizeValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string? NormalizeForLookup(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();
    }
}
