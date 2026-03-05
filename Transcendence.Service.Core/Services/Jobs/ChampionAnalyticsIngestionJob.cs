using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Transcendence.Data;
using Transcendence.Data.Models.LoL.Match;
using Transcendence.Data.Models.Service;
using Transcendence.Data.Repositories.Interfaces;
using Transcendence.Service.Core.Services.Jobs.Configuration;
using Transcendence.Service.Core.Services.Jobs.Interfaces;
using Transcendence.Service.Core.Services.Jobs.Priority;
using Transcendence.Service.Core.Services.RiotApi;

namespace Transcendence.Service.Core.Services.Jobs;

[DisableConcurrentExecution(timeoutInSeconds: 10 * 60)]
public class ChampionAnalyticsIngestionJob(
    TranscendenceContext db,
    ISummonerBootstrapService bootstrapService,
    IRefreshLockRepository refreshLockRepository,
    IBackgroundJobClient backgroundJobClient,
    IIngestionPriorityScoringPolicy scoringPolicy,
    IAdaptiveThroughputBudgetPolicy adaptiveThroughputBudgetPolicy,
    IStarvationGuardrailPolicy starvationGuardrailPolicy,
    IOptions<ChampionAnalyticsIngestionJobOptions> options,
    ILogger<ChampionAnalyticsIngestionJob> logger)
{
    private static readonly TimeSpan QueueFailureLockReleaseTimeout = TimeSpan.FromSeconds(5);

    private sealed record CandidateSummoner(
        string PlatformRegion,
        string GameName,
        string TagLine,
        DateTime UpdatedAt,
        bool IsFavorite);

    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        await ExecuteInternalAsync(rampOnly: false, ct);
    }

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

        var hasTrackedSummoners = await db.Summoners
            .AsNoTracking()
            .AnyAsync(ct);

        if (!hasTrackedSummoners)
            await bootstrapService.EnsureSeededFromChallengerAsync(ct);

        var currentPatchInfo = await db.Patches
            .AsNoTracking()
            .Where(p => p.IsActive)
            .Select(p => new { p.Version, p.ReleaseDate })
            .FirstOrDefaultAsync(ct);

        if (currentPatchInfo == null || string.IsNullOrWhiteSpace(currentPatchInfo.Version))
        {
            logger.LogWarning("Champion analytics ingestion skipped: no active patch found.");
            return;
        }

        var currentPatch = currentPatchInfo.Version;
        var releaseUtc = currentPatchInfo.ReleaseDate.Kind == DateTimeKind.Utc
            ? currentPatchInfo.ReleaseDate
            : DateTime.SpecifyKind(currentPatchInfo.ReleaseDate, DateTimeKind.Utc);
        var rampHours = Math.Max(1, jobOptions.NewPatchRampHours);
        var isRampActive = evaluationUtc < releaseUtc.AddHours(rampHours);
        if (rampOnly && !isRampActive)
        {
            logger.LogDebug("Champion analytics ingestion ramp run skipped: ramp window inactive.");
            return;
        }

        var patchStartEpoch = new DateTimeOffset(currentPatchInfo.ReleaseDate, TimeSpan.Zero).ToUnixTimeSeconds();

        var minMatchesForPatch = Math.Max(1, jobOptions.MinimumSuccessfulMatchesForCurrentPatch);
        var targetMatchesForPatch = Math.Max(minMatchesForPatch, jobOptions.TargetSuccessfulMatchesForCurrentPatch);
        var staleAfterMinutes = Math.Max(5,
            isRampActive ? jobOptions.RampDataStaleAfterMinutes : jobOptions.DataStaleAfterMinutes);
        var staleCutoffUtc = evaluationUtc.AddMinutes(-staleAfterMinutes);

        var successfulMatchesForPatch = await db.Matches
            .AsNoTracking()
            .Where(m => m.Status == FetchStatus.Success && m.Patch == currentPatch)
            .CountAsync(ct);

        var latestFetchAtUtc = await db.Matches
            .AsNoTracking()
            .Where(m => m.Status == FetchStatus.Success && m.Patch == currentPatch && m.FetchedAt != null)
            .MaxAsync(m => m.FetchedAt, ct);

        var recentSuccessWindowStartUtc = evaluationUtc.AddMinutes(-adaptiveThroughputBudgetPolicy.VelocityLookbackMinutes);
        var recentSuccessfulMatchesForPatch = await db.Matches
            .AsNoTracking()
            .Where(m => m.Status == FetchStatus.Success &&
                        m.Patch == currentPatch &&
                        m.FetchedAt != null &&
                        m.FetchedAt >= recentSuccessWindowStartUtc)
            .CountAsync(ct);

        var isStale = !latestFetchAtUtc.HasValue || latestFetchAtUtc.Value <= staleCutoffUtc;

        var baselineMaxCandidates = Math.Max(1,
            isRampActive ? jobOptions.RampMaxCandidateSummonersPerRun : jobOptions.MaxCandidateSummonersPerRun);
        var baselineMaxQueued = Math.Max(1,
            isRampActive ? jobOptions.RampMaxRefreshJobsToQueuePerRun : jobOptions.MaxRefreshJobsToQueuePerRun);
        var baselineMinQueued = Math.Clamp(
            isRampActive ? jobOptions.RampMinRefreshJobsToQueuePerRun : jobOptions.MinRefreshJobsToQueuePerRun,
            1,
            baselineMaxQueued);

        var pendingCandidateCount = await EstimatePendingCandidateCountAsync(ct);
        var budget = adaptiveThroughputBudgetPolicy.ComputeBudget(new AdaptiveThroughputBudgetInput(
            nameof(ChampionAnalyticsIngestionJob),
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

        var guardrailDecision = await EvaluateStarvationGuardrailAsync(
            evaluationUtc,
            budget.QueueTarget,
            budget.MaxCandidates,
            ct);
        var queuedTarget = guardrailDecision.QueueTarget;
        var maxCandidates = guardrailDecision.MaxCandidates;
        var forcedCatchUpActive = guardrailDecision.IsForcedCatchUpActive;

        if (queuedTarget <= 0)
        {
            logger.LogInformation(
                "Champion analytics ingestion skipped: adaptive mode {Mode} and guardrail outcome {GuardrailOutcome} produced queue target {QueueTarget} (apiPriority={ApiPriority}, coverage={Coverage:F2}, backlogAgeMinutes={BacklogAge:F1}, velocityPerHour={Velocity:F2}, pressure={Pressure:F2}, deferAgeMinutes={DeferAge:F1}, deferThresholdMinutes={DeferThreshold:F1}).",
                budget.Mode,
                guardrailDecision.Outcome,
                queuedTarget,
                apiPriorityDemandActive,
                budget.CoverageRatio,
                budget.BacklogAgeMinutes,
                budget.RecentVelocityPerHour,
                budget.CandidatePressureRatio,
                guardrailDecision.MaxEligibleDeferAgeMinutes,
                guardrailDecision.DeferAgeThresholdMinutes);
            return;
        }

        if (jobOptions.PauseWhenApiPriorityRefreshActive && apiPriorityDemandActive && !forcedCatchUpActive)
        {
            logger.LogInformation(
                "Champion analytics ingestion skipped: active high-priority API refresh demand detected while pause mode is enabled.");
            return;
        }

        var lockTtl = TimeSpan.FromMinutes(Math.Max(2, jobOptions.RefreshLockMinutes));
        var includeAllModes = budget.IncludeAllModes;

        var candidates = await GetCandidatesAsync(maxCandidates, jobOptions, releaseUtc, evaluationUtc, ct);
        if (candidates.Count == 0)
        {
            logger.LogWarning(
                "Champion analytics ingestion skipped: no candidate summoners with Riot IDs are available.");
            return;
        }

        var queued = 0;
        foreach (var candidate in candidates)
        {
            ct.ThrowIfCancellationRequested();
            if (queued >= queuedTarget) break;

            if (jobOptions.PauseWhenApiPriorityRefreshActive &&
                !forcedCatchUpActive &&
                await refreshLockRepository.AnyActiveByPrefixAsync(RefreshLockKeys.ApiPriorityRefreshPrefix, ct))
            {
                logger.LogInformation(
                    "Champion analytics ingestion stopped early after queueing {QueuedCount}/{QueuedTarget} jobs due to active high-priority API refresh demand.",
                    queued,
                    queuedTarget);
                break;
            }

            if (!PlatformRouteParser.TryParse(candidate.PlatformRegion, out var platform))
            {
                logger.LogWarning(
                    "Skipping analytics ingestion candidate due to invalid platform region {PlatformRegion} ({GameName}#{TagLine})",
                    candidate.PlatformRegion,
                    candidate.GameName,
                    candidate.TagLine);
                continue;
            }

            var lockKey = RefreshLockKeys.BuildSummonerRefreshKey(platform, candidate.GameName, candidate.TagLine);
            ct.ThrowIfCancellationRequested();
            var acquired = await refreshLockRepository.TryAcquireAsync(lockKey, lockTtl, ct);
            if (!acquired) continue;

            try
            {
                ct.ThrowIfCancellationRequested();
                backgroundJobClient.Enqueue<ISummonerRefreshJob>(job =>
                    job.RefreshForAnalytics(candidate.GameName, candidate.TagLine, platform, lockKey,
                        patchStartEpoch, currentPatch, includeAllModes, CancellationToken.None));
                queued++;
            }
            catch (OperationCanceledException)
            {
                await ReleaseLockAfterQueueFailureAsync(lockKey);
                throw;
            }
            catch (Exception)
            {
                await ReleaseLockAfterQueueFailureAsync(lockKey);
                throw;
            }
        }

        logger.LogInformation(
            "Champion analytics ingestion queued {QueuedCount}/{QueuedTarget} low-priority summoner refresh jobs (patch {Patch}, mode {Mode}, guardrail={GuardrailOutcome}, forceCatchUp={ForceCatchUp}, matches {MatchCount}/{TargetMatchCount}, stale {IsStale}, includeAllModes {IncludeAllModes}, latestFetchAt {LatestFetchAt}, ramp={Ramp}, coverage={Coverage:F2}, backlogAgeMinutes={BacklogAge:F1}, velocityPerHour={Velocity:F2}, pressure={Pressure:F2}, deferAgeMinutes={DeferAge:F1}, deferThresholdMinutes={DeferThreshold:F1}).",
            queued,
            queuedTarget,
            currentPatch,
            budget.Mode,
            guardrailDecision.Outcome,
            forcedCatchUpActive,
            successfulMatchesForPatch,
            targetMatchesForPatch,
            isStale,
            includeAllModes,
            latestFetchAtUtc,
            isRampActive,
            budget.CoverageRatio,
            budget.BacklogAgeMinutes,
            budget.RecentVelocityPerHour,
            budget.CandidatePressureRatio,
            guardrailDecision.MaxEligibleDeferAgeMinutes,
            guardrailDecision.DeferAgeThresholdMinutes);
    }

    private async Task ReleaseLockAfterQueueFailureAsync(string lockKey)
    {
        using var releaseTimeoutCts = new CancellationTokenSource(QueueFailureLockReleaseTimeout);
        try
        {
            await refreshLockRepository.ReleaseAsync(lockKey, releaseTimeoutCts.Token);
        }
        catch (OperationCanceledException) when (releaseTimeoutCts.IsCancellationRequested)
        {
            logger.LogWarning(
                "Champion analytics ingestion timed out releasing refresh lock {LockKey} after queue failure (timeout {TimeoutSeconds}s).",
                lockKey,
                QueueFailureLockReleaseTimeout.TotalSeconds);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Champion analytics ingestion failed to release refresh lock {LockKey} after queue failure.",
                lockKey);
        }
    }

    private async Task<List<CandidateSummoner>> GetCandidatesAsync(
        int maxCandidates,
        ChampionAnalyticsIngestionJobOptions jobOptions,
        DateTime patchReleaseUtc,
        DateTime evaluationUtc,
        CancellationToken ct)
    {
        var combined = new List<CandidateSummoner>();

        if (jobOptions.PrioritizeFavoriteSummoners)
        {
            var favoriteCandidates = await (
                from s in db.Summoners.AsNoTracking()
                join f in db.UserFavoriteSummoners.AsNoTracking()
                    on new { Puuid = s.Puuid!, PlatformRegion = s.PlatformRegion! }
                    equals new { Puuid = f.SummonerPuuid, PlatformRegion = f.PlatformRegion }
                where s.GameName != null
                      && s.TagLine != null
                      && s.PlatformRegion != null
                select new CandidateSummoner(
                    s.PlatformRegion!,
                    s.GameName!,
                    s.TagLine!,
                    s.UpdatedAt,
                    true)
            ).ToListAsync(ct);

            combined.AddRange(favoriteCandidates);
        }

        if (jobOptions.FallbackToTrackedSummoners)
        {
            var fallbackCandidates = await db.Summoners
                .AsNoTracking()
                .Where(s => s.GameName != null && s.TagLine != null && s.PlatformRegion != null)
                .OrderBy(s => s.UpdatedAt)
                .Take(maxCandidates * 3)
                .Select(s => new CandidateSummoner(
                    s.PlatformRegion!,
                    s.GameName!,
                    s.TagLine!,
                    s.UpdatedAt,
                    false))
                .ToListAsync(ct);

            combined.AddRange(fallbackCandidates);
        }

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

    private Task<int> EstimatePendingCandidateCountAsync(CancellationToken ct) =>
        db.Summoners
            .AsNoTracking()
            .Where(s => s.GameName != null && s.TagLine != null && s.PlatformRegion != null)
            .CountAsync(ct);

    private async Task<StarvationGuardrailDecision> EvaluateStarvationGuardrailAsync(
        DateTime evaluationUtc,
        int baselineQueueTarget,
        int baselineMaxCandidates,
        CancellationToken ct)
    {
        var producerKey = nameof(ChampionAnalyticsIngestionJob);
        var catchUpWindowKey = RefreshLockKeys.BuildStarvationGuardrailCatchUpKey(producerKey);
        var catchUpCooldownKey = RefreshLockKeys.BuildStarvationGuardrailCooldownKey(producerKey);
        var maxDeferAgeMinutes = await EstimateMaxEligibleDeferAgeMinutesAsync(evaluationUtc, ct);

        var catchUpWindowState = await refreshLockRepository.GetAsync(catchUpWindowKey, ct);
        var catchUpCooldownState = await refreshLockRepository.GetAsync(catchUpCooldownKey, ct);
        var catchUpWindowActive = IsLockActive(catchUpWindowState, evaluationUtc);
        var catchUpCooldownActive = IsLockActive(catchUpCooldownState, evaluationUtc);

        StarvationGuardrailDecision Evaluate(bool windowActive, bool cooldownActive) =>
            starvationGuardrailPolicy.Evaluate(new StarvationGuardrailInput(
                producerKey,
                evaluationUtc,
                maxDeferAgeMinutes,
                windowActive,
                cooldownActive,
                baselineQueueTarget,
                baselineMaxCandidates));

        var decision = Evaluate(catchUpWindowActive, catchUpCooldownActive);
        if (!decision.ShouldStartCatchUpWindow)
            return decision;

        var catchUpStarted = await refreshLockRepository.TryAcquireAsync(catchUpWindowKey, decision.CatchUpWindowTtl, ct);
        if (catchUpStarted)
        {
            var cooldownTtl = decision.CatchUpWindowTtl + decision.CatchUpCooldownTtl;
            await refreshLockRepository.TryAcquireAsync(catchUpCooldownKey, cooldownTtl, ct);
            return Evaluate(windowActive: true, cooldownActive: true);
        }

        catchUpWindowState = await refreshLockRepository.GetAsync(catchUpWindowKey, ct);
        catchUpCooldownState = await refreshLockRepository.GetAsync(catchUpCooldownKey, ct);
        return Evaluate(
            IsLockActive(catchUpWindowState, evaluationUtc),
            IsLockActive(catchUpCooldownState, evaluationUtc));
    }

    private async Task<double?> EstimateMaxEligibleDeferAgeMinutesAsync(DateTime evaluationUtc, CancellationToken ct)
    {
        var oldestUpdatedAt = await db.Summoners
            .AsNoTracking()
            .Where(s => s.GameName != null && s.TagLine != null && s.PlatformRegion != null)
            .Select(s => (DateTime?)s.UpdatedAt)
            .MinAsync(ct);

        if (!oldestUpdatedAt.HasValue)
            return null;

        var oldestUtc = EnsureUtc(oldestUpdatedAt.Value);
        var resolvedEvaluationUtc = EnsureUtc(evaluationUtc);
        return Math.Max(0d, (resolvedEvaluationUtc - oldestUtc).TotalMinutes);
    }

    private static bool IsLockActive(RefreshLock? refreshLock, DateTime evaluationUtc)
    {
        if (refreshLock == null)
            return false;

        return EnsureUtc(refreshLock.LockedUntilUtc) > EnsureUtc(evaluationUtc);
    }

    private static DateTime EnsureUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
