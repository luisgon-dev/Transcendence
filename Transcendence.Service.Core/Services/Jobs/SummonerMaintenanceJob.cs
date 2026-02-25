using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Transcendence.Data;
using Transcendence.Data.Models.LoL.Match;
using Transcendence.Data.Repositories.Interfaces;
using Transcendence.Service.Core.Services.Jobs.Configuration;
using Transcendence.Service.Core.Services.Jobs.Interfaces;
using Transcendence.Service.Core.Services.RiotApi;

namespace Transcendence.Service.Core.Services.Jobs;

[DisableConcurrentExecution(timeoutInSeconds: 10 * 60)]
public class SummonerMaintenanceJob(
    TranscendenceContext db,
    IBackgroundJobClient backgroundJobClient,
    IRefreshLockRepository refreshLockRepository,
    IOptions<SummonerMaintenanceJobOptions> options,
    IOptions<ChampionAnalyticsIngestionJobOptions> analyticsOptions,
    ILogger<SummonerMaintenanceJob> logger)
{
    private sealed record CandidateSummoner(
        string PlatformRegion,
        string GameName,
        string TagLine,
        DateTime UpdatedAt);

    [Queue("refresh-low")]
    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        var jobOptions = options.Value;
        if (jobOptions.PauseWhenApiPriorityRefreshActive &&
            await refreshLockRepository.AnyActiveByPrefixAsync(RefreshLockKeys.ApiPriorityRefreshPrefix, ct))
        {
            logger.LogInformation("[Maintenance] Skipped due to active high-priority API refresh demand.");
            return;
        }

        var activePatch = await db.Patches
            .AsNoTracking()
            .Where(p => p.IsActive)
            .Select(p => new { p.Version, p.ReleaseDate })
            .FirstOrDefaultAsync(ct);

        if (activePatch == null || string.IsNullOrWhiteSpace(activePatch.Version))
        {
            logger.LogWarning("[Maintenance] Skipped because no active patch exists.");
            return;
        }

        var patchStartEpoch = new DateTimeOffset(activePatch.ReleaseDate, TimeSpan.Zero).ToUnixTimeSeconds();
        var successfulMatchesForPatch = await db.Matches
            .AsNoTracking()
            .Where(m => m.Status == FetchStatus.Success && m.Patch == activePatch.Version)
            .CountAsync(ct);

        var includeAllModes = successfulMatchesForPatch >=
                              Math.Max(
                                  analyticsOptions.Value.MinimumSuccessfulMatchesForCurrentPatch,
                                  analyticsOptions.Value.TargetSuccessfulMatchesForCurrentPatch);

        var staleCutoffUtc = DateTime.UtcNow.AddMinutes(-Math.Max(5, jobOptions.DataStaleAfterMinutes));
        var maxCandidates = Math.Max(1, jobOptions.MaxCandidateSummonersPerRun);
        var maxQueued = Math.Max(1, jobOptions.MaxRefreshJobsToQueuePerRun);
        var lockTtl = TimeSpan.FromMinutes(Math.Max(2, jobOptions.RefreshLockMinutes));

        var candidates = await GetCandidatesAsync(staleCutoffUtc, maxCandidates, jobOptions, ct);
        if (candidates.Count == 0)
        {
            logger.LogInformation("[Maintenance] No stale summoner candidates were eligible.");
            return;
        }

        var queued = 0;
        foreach (var candidate in candidates)
        {
            if (queued >= maxQueued)
                break;

            if (!PlatformRouteParser.TryParse(candidate.PlatformRegion, out var platform))
            {
                logger.LogWarning(
                    "[Maintenance] Skipping candidate due to invalid platform region {PlatformRegion} ({GameName}#{TagLine}).",
                    candidate.PlatformRegion,
                    candidate.GameName,
                    candidate.TagLine);
                continue;
            }

            var lockKey = RefreshLockKeys.BuildSummonerRefreshKey(platform, candidate.GameName, candidate.TagLine);
            var acquired = await refreshLockRepository.TryAcquireAsync(lockKey, lockTtl, ct);
            if (!acquired)
                continue;

            try
            {
                backgroundJobClient.Enqueue<ISummonerRefreshJob>(job =>
                    job.RefreshForAnalytics(
                        candidate.GameName,
                        candidate.TagLine,
                        platform,
                        lockKey,
                        patchStartEpoch,
                        activePatch.Version,
                        includeAllModes,
                        CancellationToken.None));
                queued++;
            }
            catch (Exception)
            {
                await refreshLockRepository.ReleaseAsync(lockKey, ct);
                throw;
            }
        }

        logger.LogInformation(
            "[Maintenance] Queued {Queued}/{Target} refresh jobs. includeAllModes={IncludeAllModes}, patch={Patch}, coverage={Coverage}.",
            queued,
            maxQueued,
            includeAllModes,
            activePatch.Version,
            successfulMatchesForPatch);
    }

    private async Task<List<CandidateSummoner>> GetCandidatesAsync(
        DateTime staleCutoffUtc,
        int maxCandidates,
        SummonerMaintenanceJobOptions options,
        CancellationToken ct)
    {
        var combined = new List<CandidateSummoner>();

        if (options.PrioritizeFavoriteSummoners)
        {
            var favoriteCandidates = await (
                from s in db.Summoners.AsNoTracking()
                join f in db.UserFavoriteSummoners.AsNoTracking()
                    on new { Puuid = s.Puuid!, PlatformRegion = s.PlatformRegion! }
                    equals new { Puuid = f.SummonerPuuid, PlatformRegion = f.PlatformRegion }
                where s.GameName != null
                      && s.TagLine != null
                      && s.PlatformRegion != null
                      && s.UpdatedAt <= staleCutoffUtc
                select new CandidateSummoner(
                    s.PlatformRegion!,
                    s.GameName!,
                    s.TagLine!,
                    s.UpdatedAt)
            ).ToListAsync(ct);

            combined.AddRange(favoriteCandidates);
        }

        var trackedCandidates = await db.Summoners
            .AsNoTracking()
            .Where(s => s.GameName != null && s.TagLine != null && s.PlatformRegion != null)
            .Where(s => s.UpdatedAt <= staleCutoffUtc)
            .OrderBy(s => s.UpdatedAt)
            .Take(maxCandidates * 3)
            .Select(s => new CandidateSummoner(
                s.PlatformRegion!,
                s.GameName!,
                s.TagLine!,
                s.UpdatedAt))
            .ToListAsync(ct);

        combined.AddRange(trackedCandidates);

        return combined
            .OrderBy(c => c.UpdatedAt)
            .DistinctBy(c =>
                $"{c.PlatformRegion.ToUpperInvariant()}:{c.GameName.ToUpperInvariant()}:{c.TagLine.ToUpperInvariant()}")
            .Take(maxCandidates)
            .ToList();
    }
}
