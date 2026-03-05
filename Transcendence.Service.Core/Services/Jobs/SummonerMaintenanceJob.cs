using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Transcendence.Data;
using Transcendence.Data.Models.LoL.Match;
using Transcendence.Data.Repositories.Interfaces;
using Transcendence.Service.Core.Services.Jobs.Configuration;
using Transcendence.Service.Core.Services.Jobs.Interfaces;
using Transcendence.Service.Core.Services.Jobs.Priority;
using Transcendence.Service.Core.Services.RiotApi;

namespace Transcendence.Service.Core.Services.Jobs;

[DisableConcurrentExecution(timeoutInSeconds: 10 * 60)]
public class SummonerMaintenanceJob(
    TranscendenceContext db,
    IBackgroundJobClient backgroundJobClient,
    IRefreshLockRepository refreshLockRepository,
    IIngestionPriorityScoringPolicy scoringPolicy,
    IAdaptiveThroughputBudgetPolicy adaptiveThroughputBudgetPolicy,
    IOptions<SummonerMaintenanceJobOptions> options,
    IOptions<ChampionAnalyticsIngestionJobOptions> analyticsOptions,
    ILogger<SummonerMaintenanceJob> logger)
{
    private sealed record CandidateSummoner(
        string PlatformRegion,
        string GameName,
        string TagLine,
        DateTime UpdatedAt,
        bool IsFavorite);

    [Queue("refresh-low")]
    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        await ExecuteInternalAsync(rampOnly: false, ct);
    }

    [Queue("refresh-low")]
    public async Task ExecuteRampAsync(CancellationToken ct = default)
    {
        await ExecuteInternalAsync(rampOnly: true, ct);
    }

    private async Task ExecuteInternalAsync(bool rampOnly, CancellationToken ct = default)
    {
        var jobOptions = options.Value;
        var evaluationUtc = DateTime.UtcNow;
        var apiPriorityDemandActive = await refreshLockRepository.AnyActiveByPrefixAsync(
            RefreshLockKeys.ApiPriorityRefreshPrefix,
            ct);

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

        var releaseUtc = activePatch.ReleaseDate.Kind == DateTimeKind.Utc
            ? activePatch.ReleaseDate
            : DateTime.SpecifyKind(activePatch.ReleaseDate, DateTimeKind.Utc);
        var rampHours = Math.Max(1, jobOptions.NewPatchRampHours);
        var isRampActive = evaluationUtc < releaseUtc.AddHours(rampHours);
        if (rampOnly && !isRampActive)
        {
            logger.LogDebug("[Maintenance] Ramp run skipped: ramp window inactive.");
            return;
        }

        var patchStartEpoch = new DateTimeOffset(activePatch.ReleaseDate, TimeSpan.Zero).ToUnixTimeSeconds();
        var successfulMatchesForPatch = await db.Matches
            .AsNoTracking()
            .Where(m => m.Status == FetchStatus.Success && m.Patch == activePatch.Version)
            .CountAsync(ct);

        var targetMatchesForPatch = Math.Max(
            analyticsOptions.Value.MinimumSuccessfulMatchesForCurrentPatch,
            analyticsOptions.Value.TargetSuccessfulMatchesForCurrentPatch);

        var latestFetchAtUtc = await db.Matches
            .AsNoTracking()
            .Where(m => m.Status == FetchStatus.Success && m.Patch == activePatch.Version && m.FetchedAt != null)
            .MaxAsync(m => m.FetchedAt, ct);

        var staleAfterMinutes = Math.Max(5,
            isRampActive ? jobOptions.RampDataStaleAfterMinutes : jobOptions.DataStaleAfterMinutes);
        var staleCutoffUtc = evaluationUtc.AddMinutes(-staleAfterMinutes);
        var baselineMaxCandidates = Math.Max(1,
            isRampActive ? jobOptions.RampMaxCandidateSummonersPerRun : jobOptions.MaxCandidateSummonersPerRun);
        var baselineMaxQueued = Math.Max(1,
            isRampActive ? jobOptions.RampMaxRefreshJobsToQueuePerRun : jobOptions.MaxRefreshJobsToQueuePerRun);
        var baselineMinQueued = 1;

        var recentSuccessWindowStartUtc = evaluationUtc.AddMinutes(-adaptiveThroughputBudgetPolicy.VelocityLookbackMinutes);
        var recentSuccessfulMatchesForPatch = await db.Matches
            .AsNoTracking()
            .Where(m => m.Status == FetchStatus.Success &&
                        m.Patch == activePatch.Version &&
                        m.FetchedAt != null &&
                        m.FetchedAt >= recentSuccessWindowStartUtc)
            .CountAsync(ct);
        var pendingCandidateCount = await EstimatePendingCandidateCountAsync(staleCutoffUtc, ct);

        var budget = adaptiveThroughputBudgetPolicy.ComputeBudget(new AdaptiveThroughputBudgetInput(
            nameof(SummonerMaintenanceJob),
            evaluationUtc,
            apiPriorityDemandActive,
            successfulMatchesForPatch,
            targetMatchesForPatch,
            latestFetchAtUtc,
            recentSuccessfulMatchesForPatch,
            pendingCandidateCount,
            baselineMaxCandidates,
            baselineMinQueued,
            baselineMaxQueued));

        if (budget.QueueTarget <= 0)
        {
            logger.LogInformation(
                "[Maintenance] Skipped because adaptive mode {Mode} produced queue target {QueueTarget} (apiPriority={ApiPriority}, coverage={Coverage:F2}, backlogAgeMinutes={BacklogAge:F1}, velocityPerHour={Velocity:F2}, pressure={Pressure:F2}).",
                budget.Mode,
                budget.QueueTarget,
                apiPriorityDemandActive,
                budget.CoverageRatio,
                budget.BacklogAgeMinutes,
                budget.RecentVelocityPerHour,
                budget.CandidatePressureRatio);
            return;
        }

        if (jobOptions.PauseWhenApiPriorityRefreshActive && apiPriorityDemandActive)
        {
            logger.LogInformation("[Maintenance] Skipped due to active high-priority API refresh demand.");
            return;
        }

        var maxCandidates = budget.MaxCandidates;
        var maxQueued = budget.QueueTarget;
        var includeAllModes = budget.IncludeAllModes;
        var lockTtl = TimeSpan.FromMinutes(Math.Max(2, jobOptions.RefreshLockMinutes));

        var candidates = await GetCandidatesAsync(staleCutoffUtc, maxCandidates, releaseUtc, evaluationUtc, jobOptions, ct);
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
            "[Maintenance] Queued {Queued}/{Target} refresh jobs. includeAllModes={IncludeAllModes}, patch={Patch}, mode={Mode}, coverage={Coverage}, ramp={Ramp}, backlogAgeMinutes={BacklogAge:F1}, velocityPerHour={Velocity:F2}, pressure={Pressure:F2}.",
            queued,
            maxQueued,
            includeAllModes,
            activePatch.Version,
            budget.Mode,
            successfulMatchesForPatch,
            isRampActive,
            budget.BacklogAgeMinutes,
            budget.RecentVelocityPerHour,
            budget.CandidatePressureRatio);
    }

    private async Task<List<CandidateSummoner>> GetCandidatesAsync(
        DateTime staleCutoffUtc,
        int maxCandidates,
        DateTime patchReleaseUtc,
        DateTime evaluationUtc,
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
                    s.UpdatedAt,
                    true)
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
                s.UpdatedAt,
                false))
            .ToListAsync(ct);

        combined.AddRange(trackedCandidates);

        var rankedCandidates = scoringPolicy.RankCandidates(
            combined,
            candidate => new IngestionPriorityCandidate(
                RefreshLockKeys.BuildCanonicalIdentity(candidate.PlatformRegion, candidate.GameName, candidate.TagLine),
                candidate.UpdatedAt,
                candidate.IsFavorite),
            new IngestionPriorityContext(patchReleaseUtc, evaluationUtc),
            maxCandidates);

        return rankedCandidates.ToList();
    }

    private Task<int> EstimatePendingCandidateCountAsync(DateTime staleCutoffUtc, CancellationToken ct) =>
        db.Summoners
            .AsNoTracking()
            .Where(s => s.GameName != null && s.TagLine != null && s.PlatformRegion != null)
            .Where(s => s.UpdatedAt <= staleCutoffUtc)
            .CountAsync(ct);
}
