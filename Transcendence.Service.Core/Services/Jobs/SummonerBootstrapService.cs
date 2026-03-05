using Camille.Enums;
using Camille.RiotGames;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Transcendence.Data;
using Transcendence.Data.Models.LoL.Account;
using Transcendence.Data.Repositories.Interfaces;
using Transcendence.Service.Core.Services.Jobs.Configuration;
using Transcendence.Service.Core.Services.Jobs.Interfaces;
using Transcendence.Service.Core.Services.RiotApi;

namespace Transcendence.Service.Core.Services.Jobs;

public class SummonerBootstrapService(
    RiotGamesApi riotApi,
    TranscendenceContext db,
    ISummonerRepository summonerRepository,
    IRefreshLockRepository refreshLockRepository,
    IOptions<SummonerBootstrapOptions> options,
    IOptions<MultiRegionIngestionOptions> multiRegionOptions,
    ILogger<SummonerBootstrapService> logger) : ISummonerBootstrapService
{
    public async Task<int> EnsureSeededFromChallengerAsync(CancellationToken ct = default)
    {
        var bootstrapOptions = options.Value;
        var multiRegion = multiRegionOptions.Value;

        if (!bootstrapOptions.Enabled)
            return 0;

        // If multi-region is enabled and has regions configured, use multi-region bootstrap
        if (multiRegion.Enabled && multiRegion.Regions.Count > 0)
            return await EnsureSeededMultiRegionAsync(multiRegion, bootstrapOptions, ct);

        // Legacy single-region bootstrap
        return await EnsureSeededSingleRegionAsync(bootstrapOptions, ct);
    }

    private async Task<int> EnsureSeededMultiRegionAsync(
        MultiRegionIngestionOptions multiRegion,
        SummonerBootstrapOptions bootstrapOptions,
        CancellationToken ct)
    {
        var enabledRegions = multiRegion.Regions
            .Where(r => r.Enabled)
            .ToList();

        if (enabledRegions.Count == 0)
        {
            logger.LogDebug("Multi-region bootstrap skipped: no enabled regions.");
            return 0;
        }

        var semaphore = new SemaphoreSlim(Math.Max(1, multiRegion.MaxConcurrentRegionBootstraps));
        var totalSeeded = 0;

        var tasks = enabledRegions.Select(async regionConfig =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                var seeded = await SeedRegionAsync(regionConfig, bootstrapOptions.LockMinutes, ct);
                Interlocked.Add(ref totalSeeded, seeded);
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);

        if (totalSeeded > 0)
            logger.LogInformation(
                "Multi-region bootstrap complete: seeded {TotalSeeded} summoners across {RegionCount} regions.",
                totalSeeded,
                enabledRegions.Count);

        return totalSeeded;
    }

    private async Task<int> SeedRegionAsync(RegionConfig regionConfig, int lockMinutes, CancellationToken ct)
    {
        if (!PlatformRouteParser.TryParse(regionConfig.Region, out var platform))
        {
            logger.LogWarning(
                "Multi-region bootstrap skipping region: invalid platform region {PlatformRegion}.",
                regionConfig.Region);
            return 0;
        }

        // Check if this specific region already has summoners
        var hasRegionSummoners = await db.Summoners
            .AsNoTracking()
            .AnyAsync(s => s.PlatformRegion == regionConfig.Region, ct);

        if (hasRegionSummoners)
        {
            logger.LogDebug(
                "Multi-region bootstrap skipped for {Platform}: summoners already exist.",
                platform);
            return 0;
        }

        var seedCount = Math.Max(1, regionConfig.ChallengerSeedCount);
        var lockTtl = TimeSpan.FromMinutes(Math.Max(1, lockMinutes));
        var lockKey = $"summoner-bootstrap:challenger:{platform}";

        var acquired = await refreshLockRepository.TryAcquireAsync(lockKey, lockTtl, ct);
        if (!acquired)
        {
            logger.LogDebug("Multi-region bootstrap skipped for {Platform}: lock held ({LockKey}).", platform, lockKey);
            return 0;
        }

        try
        {
            // Re-check once we hold the lock
            hasRegionSummoners = await db.Summoners
                .AsNoTracking()
                .AnyAsync(s => s.PlatformRegion == regionConfig.Region, ct);

            if (hasRegionSummoners)
            {
                logger.LogDebug(
                    "Multi-region bootstrap skipped for {Platform}: summoners already exist (post-lock).",
                    platform);
                return 0;
            }

            return await SeedChallengerPlayersAsync(platform, seedCount, ct);
        }
        finally
        {
            try
            {
                await refreshLockRepository.ReleaseAsync(lockKey, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Multi-region bootstrap failed to release lock {LockKey}.", lockKey);
            }
        }
    }

    private async Task<int> EnsureSeededSingleRegionAsync(SummonerBootstrapOptions bootstrapOptions, CancellationToken ct)
    {
        // Only seed on truly empty installs
        var hasAnySummoners = await db.Summoners.AsNoTracking().AnyAsync(ct);
        if (hasAnySummoners)
        {
            logger.LogDebug("Summoner bootstrap skipped: summoners already exist.");
            return 0;
        }

        if (!PlatformRouteParser.TryParse(bootstrapOptions.PlatformRegion, out var platform))
        {
            logger.LogWarning(
                "Summoner bootstrap skipped: invalid platform region {PlatformRegion}.",
                bootstrapOptions.PlatformRegion);
            return 0;
        }

        var seedCount = Math.Max(1, bootstrapOptions.ChallengerSeedCount);
        var lockTtl = TimeSpan.FromMinutes(Math.Max(1, bootstrapOptions.LockMinutes));
        var lockKey = $"summoner-bootstrap:challenger:{platform}";

        var acquired = await refreshLockRepository.TryAcquireAsync(lockKey, lockTtl, ct);
        if (!acquired)
        {
            logger.LogDebug("Summoner bootstrap skipped: lock held ({LockKey}).", lockKey);
            return 0;
        }

        try
        {
            // Re-check once we hold the lock
            hasAnySummoners = await db.Summoners.AsNoTracking().AnyAsync(ct);
            if (hasAnySummoners)
            {
                logger.LogDebug("Summoner bootstrap skipped: summoners already exist.");
                return 0;
            }

            return await SeedChallengerPlayersAsync(platform, seedCount, ct);
        }
        finally
        {
            try
            {
                await refreshLockRepository.ReleaseAsync(lockKey, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Summoner bootstrap failed to release lock {LockKey}.", lockKey);
            }
        }
    }

    private async Task<int> SeedChallengerPlayersAsync(PlatformRoute platform, int seedCount, CancellationToken ct)
    {
        logger.LogInformation(
            "Summoner bootstrap starting: seeding {SeedCount} challenger summoners from {Platform}.",
            seedCount,
            platform);

        var challengerLeague = await riotApi.LeagueV4()
            .GetChallengerLeagueAsync(platform, QueueType.RANKED_SOLO_5x5, ct);

        var puuids = challengerLeague.Entries
            .Where(e => e is not null && !string.IsNullOrWhiteSpace(e.Puuid))
            .OrderByDescending(e => e.LeaguePoints)
            .Select(e => e.Puuid!.Trim())
            .Distinct(StringComparer.Ordinal)
            .Take(seedCount)
            .ToList();

        if (puuids.Count == 0)
        {
            logger.LogWarning(
                "Summoner bootstrap ended: Challenger league returned 0 valid entries for {Platform}.",
                platform);
            return 0;
        }

        var seeded = 0;
        foreach (var puuid in puuids)
        {
            try
            {
                var summonerV4 = await riotApi.SummonerV4().GetByPUUIDAsync(platform, puuid, ct);
                var account = await riotApi.AccountV1().GetByPuuidAsync(platform.ToRegional(), puuid, ct);

                if (account == null ||
                    string.IsNullOrWhiteSpace(account.GameName) ||
                    string.IsNullOrWhiteSpace(account.TagLine))
                {
                    logger.LogInformation(
                        "Summoner bootstrap skipping PUUID {Puuid}: Riot account missing gameName/tagLine.",
                        puuid);
                    continue;
                }

                var entity = new Summoner
                {
                    Id = Guid.NewGuid(),
                    RiotSummonerId = summonerV4.Id,
                    SummonerName = $"{account.GameName}#{account.TagLine}",
                    ProfileIconId = summonerV4.ProfileIconId,
                    SummonerLevel = summonerV4.SummonerLevel,
                    RevisionDate = summonerV4.RevisionDate,
                    Puuid = summonerV4.Puuid,
                    GameName = account.GameName,
                    TagLine = account.TagLine,
                    AccountId = summonerV4.AccountId,
                    PlatformRegion = platform.ToString(),
                    Region = platform.ToRegional().ToString(),
                    UpdatedAt = DateTime.UtcNow,
                    Ranks = []
                };

                await summonerRepository.AddOrUpdateSummonerAsync(entity, ct);
                seeded++;
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "Summoner bootstrap failed for PUUID {Puuid} on {Platform}.",
                    puuid,
                    platform);
            }
        }

        if (seeded > 0)
            await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Summoner bootstrap complete: seeded {SeededCount} summoners from {Platform}.",
            seeded,
            platform);

        return seeded;
    }
}
