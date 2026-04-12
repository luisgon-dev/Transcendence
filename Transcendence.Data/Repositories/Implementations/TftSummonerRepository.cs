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
                s.PlatformRegion == normalizedPlatformRegion &&
                s.GameName != null &&
                s.TagLine != null &&
                s.GameNameNormalized != null &&
                s.TagLineNormalized != null &&
                context.TftMatchParticipants.Any(p => p.SummonerId == s.Id) &&
                EF.Functions.Like(s.GameNameNormalized, gameLike));

        if (tagLike != null)
            query = query.Where(s => EF.Functions.Like(s.TagLineNormalized!, tagLike));

        IQueryable<TftSummoner> orderedQuery;

        if (normalizedTagLinePrefix != null)
        {
            orderedQuery = query
                .OrderBy(s => s.GameNameNormalized == normalizedGameNamePrefix ? 0 : 1)
                .ThenBy(s => s.TagLineNormalized == normalizedTagLinePrefix ? 0 : 1)
                .ThenBy(s => s.GameName)
                .ThenBy(s => s.TagLine);
        }
        else
        {
            orderedQuery = query
                .OrderBy(s => s.GameNameNormalized == normalizedGameNamePrefix ? 0 : 1)
                .ThenBy(s => s.GameName)
                .ThenBy(s => s.TagLine);
        }

        return await orderedQuery
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

    public async Task<IReadOnlyList<TftSummoner>> FindByRiotIdsAsync(
        string platformRegion,
        IReadOnlyList<(string GameName, string TagLine)> riotIds,
        CancellationToken cancellationToken = default)
    {
        var normalizedPlatform = NormalizeForLookup(platformRegion);
        if (normalizedPlatform == null || riotIds.Count == 0)
            return [];

        // Build normalized lookup keys
        var normalizedIds = riotIds
            .Select(id => (
                GameName: NormalizeForLookup(id.GameName),
                TagLine: NormalizeForLookup(id.TagLine)))
            .Where(id => id.GameName != null && id.TagLine != null)
            .ToList();

        if (normalizedIds.Count == 0)
            return [];

        // Get all potential matches for the platform, then filter in memory
        // This is more efficient than N+1 queries for small result sets
        var gameNames = normalizedIds.Select(id => id.GameName).Distinct().ToList();
        
        var summoners = await context.TftSummoners
            .AsNoTracking()
            .Include(x => x.Ranks)
            .Where(s => s.PlatformRegion == normalizedPlatform && 
                        gameNames.Contains(s.GameNameNormalized))
            .ToListAsync(cancellationToken);

        // Filter to exact matches in memory
        var idSet = normalizedIds
            .Select(id => $"{id.GameName}#{id.TagLine}")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return summoners
            .Where(s => idSet.Contains($"{s.GameNameNormalized}#{s.TagLineNormalized}"))
            .ToList();
    }

    public async Task<TftSummoner> AddOrUpdateAsync(TftSummoner summoner, CancellationToken cancellationToken = default)
    {
        summoner.GameName = NormalizeValue(summoner.GameName);
        summoner.TagLine = NormalizeValue(summoner.TagLine);
        summoner.GameNameNormalized = NormalizeForLookup(summoner.GameName);
        summoner.TagLineNormalized = NormalizeForLookup(summoner.TagLine);
        summoner.PlatformRegion = summoner.PlatformRegion.Trim().ToUpperInvariant();
        summoner.UpdatedAt = DateTime.UtcNow;

        var existing = await FindExistingForUpsertAsync(summoner, cancellationToken);

        if (existing == null)
        {
            if (summoner.Id == Guid.Empty)
                summoner.Id = Guid.NewGuid();

            context.TftSummoners.Add(summoner);

            try
            {
                await context.SaveChangesAsync(cancellationToken);
                existing = summoner;
            }
            catch (DbUpdateException)
            {
                var resolved = await TryResolveConcurrentInsertAsync(summoner, cancellationToken);
                if (!resolved)
                    throw;

                existing = await FindExistingForUpsertAsync(summoner, cancellationToken);
                if (existing == null)
                    throw;

                ApplyUpdates(existing, summoner);
                await context.SaveChangesAsync(cancellationToken);
            }
        }
        else
        {
            ApplyUpdates(existing, summoner);
            await context.SaveChangesAsync(cancellationToken);
        }

        if (summoner.Ranks.Count > 0)
        {
            await tftRankRepository.AddOrUpdateRankAsync(existing, summoner.Ranks.ToList(), cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
        }

        return existing;
    }

    private async Task<TftSummoner?> FindExistingForUpsertAsync(TftSummoner summoner, CancellationToken cancellationToken)
    {
        return await context.TftSummoners
            .Include(x => x.Ranks)
            .FirstOrDefaultAsync(x =>
                    x.Puuid == summoner.Puuid
                    || (x.PlatformRegion == summoner.PlatformRegion
                        && x.GameNameNormalized == summoner.GameNameNormalized
                        && x.TagLineNormalized == summoner.TagLineNormalized),
                cancellationToken);
    }

    private async Task<bool> TryResolveConcurrentInsertAsync(TftSummoner summoner, CancellationToken cancellationToken)
    {
        context.Entry(summoner).State = EntityState.Detached;
        foreach (var rank in summoner.Ranks)
            context.Entry(rank).State = EntityState.Detached;

        var existing = await FindExistingForUpsertAsync(summoner, cancellationToken);
        return existing != null;
    }

    private static void ApplyUpdates(TftSummoner existing, TftSummoner summoner)
    {
        existing.Puuid = summoner.Puuid;
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
