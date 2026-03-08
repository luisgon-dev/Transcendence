using Microsoft.EntityFrameworkCore;
using Transcendence.Data.Models.Tft.Account;
using Transcendence.Data.Repositories.Interfaces;

namespace Transcendence.Data.Repositories.Implementations;

public class TftSummonerRepository(TranscendenceContext context, ITftRankRepository tftRankRepository)
    : ITftSummonerRepository
{
    public async Task<IReadOnlyList<TftSummonerSearchCandidate>> SearchByPrefixAsync(
        string platformRegion,
        string gameNamePrefix,
        string? tagLinePrefix,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var normalizedPlatformRegion = NormalizeForLookup(platformRegion);
        var normalizedGameNamePrefix = NormalizeForLookup(gameNamePrefix);
        var normalizedTagLinePrefix = NormalizeForLookup(tagLinePrefix);
        var safeLimit = Math.Clamp(limit, 1, 25);

        if (normalizedPlatformRegion == null || normalizedGameNamePrefix == null)
            return [];

        var gameLike = $"{normalizedGameNamePrefix}%";
        var tagLike = normalizedTagLinePrefix == null ? null : $"{normalizedTagLinePrefix}%";

        var query = context.TftSummoners
            .AsNoTracking()
            .Where(s =>
                s.PlatformRegion == platformRegion.Trim().ToUpperInvariant() &&
                s.GameName != null &&
                s.TagLine != null &&
                s.GameNameNormalized != null &&
                s.TagLineNormalized != null &&
                context.TftMatchParticipants.Any(p => p.SummonerId == s.Id) &&
                EF.Functions.Like(s.GameNameNormalized, gameLike));

        if (tagLike != null)
            query = query.Where(s => EF.Functions.Like(s.TagLineNormalized!, tagLike));

        return await query
            .OrderBy(s => s.GameNameNormalized == normalizedGameNamePrefix ? 0 : 1)
            .ThenBy(s => s.GameName)
            .ThenBy(s => s.TagLine)
            .Take(safeLimit)
            .Select(s => new TftSummonerSearchCandidate(
                s.PlatformRegion,
                s.GameName!,
                s.TagLine!,
                s.ProfileIconId))
            .ToListAsync(cancellationToken);
    }

    public async Task<TftSummoner?> GetByPuuidAsync(
        string puuid,
        Func<IQueryable<TftSummoner>, IQueryable<TftSummoner>>? includes = null,
        CancellationToken cancellationToken = default)
    {
        IQueryable<TftSummoner> query = context.TftSummoners;
        if (includes != null)
            query = includes(query);

        return await query.FirstOrDefaultAsync(x => x.Puuid == puuid, cancellationToken);
    }

    public async Task<TftSummoner?> FindByRiotIdAsync(
        string platformRegion,
        string gameName,
        string tagLine,
        Func<IQueryable<TftSummoner>, IQueryable<TftSummoner>>? includes = null,
        CancellationToken cancellationToken = default)
    {
        IQueryable<TftSummoner> query = context.TftSummoners;
        if (includes != null)
            query = includes(query);

        var normalizedPlatform = NormalizeValue(platformRegion)?.ToUpperInvariant();
        var normalizedGameName = NormalizeValue(gameName);
        var normalizedTag = NormalizeValue(tagLine);
        var gameKey = NormalizeForLookup(normalizedGameName);
        var tagKey = NormalizeForLookup(normalizedTag);
        if (normalizedPlatform == null || gameKey == null || tagKey == null)
            return null;

        return await query.FirstOrDefaultAsync(x =>
            x.PlatformRegion == normalizedPlatform
            && x.GameNameNormalized == gameKey
            && x.TagLineNormalized == tagKey, cancellationToken);
    }

    public async Task<TftSummoner> AddOrUpdateAsync(TftSummoner summoner, CancellationToken cancellationToken = default)
    {
        summoner.GameName = NormalizeValue(summoner.GameName);
        summoner.TagLine = NormalizeValue(summoner.TagLine);
        summoner.GameNameNormalized = NormalizeForLookup(summoner.GameName);
        summoner.TagLineNormalized = NormalizeForLookup(summoner.TagLine);
        summoner.PlatformRegion = summoner.PlatformRegion.Trim().ToUpperInvariant();
        summoner.UpdatedAt = DateTime.UtcNow;

        var existing = await context.TftSummoners
            .Include(x => x.Ranks)
            .FirstOrDefaultAsync(x => x.Puuid == summoner.Puuid, cancellationToken);

        if (existing == null)
        {
            if (summoner.Id == Guid.Empty)
                summoner.Id = Guid.NewGuid();
            context.TftSummoners.Add(summoner);
            await context.SaveChangesAsync(cancellationToken);
            existing = await context.TftSummoners.Include(x => x.Ranks).SingleAsync(x => x.Id == summoner.Id, cancellationToken);
        }
        else
        {
            existing.RiotSummonerId = summoner.RiotSummonerId;
            existing.ProfileIconId = summoner.ProfileIconId;
            existing.SummonerLevel = summoner.SummonerLevel;
            existing.RevisionDate = summoner.RevisionDate;
            existing.GameName = summoner.GameName;
            existing.TagLine = summoner.TagLine;
            existing.GameNameNormalized = summoner.GameNameNormalized;
            existing.TagLineNormalized = summoner.TagLineNormalized;
            existing.AccountId = summoner.AccountId;
            existing.PlatformRegion = summoner.PlatformRegion;
            existing.Region = summoner.Region;
            existing.UpdatedAt = DateTime.UtcNow;
            await context.SaveChangesAsync(cancellationToken);
        }

        if (summoner.Ranks.Count > 0)
        {
            await tftRankRepository.AddOrUpdateRankAsync(existing, summoner.Ranks.ToList(), cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
        }

        return existing;
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
