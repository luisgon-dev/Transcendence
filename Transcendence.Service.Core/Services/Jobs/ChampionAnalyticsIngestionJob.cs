using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Transcendence.Data;
using Transcendence.Data.Models.LoL.Match;
using Transcendence.Data.Models.Service;
using Transcendence.Data.Repositories.Interfaces;
using Transcendence.Service.Core.Services.Diagnostics;
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
    IIngestionThroughputTelemetry ingestionThroughputTelemetry,
    IOptions<ChampionAnalyticsIngestionJobOptions> options,
    IOptions<MultiRegionIngestionOptions> multiRegionOptions,
    ILogger<ChampionAnalyticsIngestionJob> logger)
{
    private static readonly TimeSpan QueueFailureLockReleaseTimeout = TimeSpan.FromSeconds(5);
    private const string ProducerKeyBase = nameof(ChampionAnalyticsIngestionJob);
    private const string TelemetrySource = "champion-analytics-ingestion-job";

    private sealed record CandidateSummoner(
        string PlatformRegion,
        string GameName,
        string TagLine,
        DateTime UpdatedAt,
        bool IsFavorite);

    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        var multiRegion = multiRegionOptions.Value;

        if (multiRegion.Enabled && multiRegion.Regions.Count > 0)
        {
            var enabledRegions = GetConfiguredEnabledRegions(multiRegion);
            foreach (var region in enabledRegions)
            {
                backgroundJobClient.Enqueue<ChampionAnalyticsIngestionJob>(
                    job => job.ExecuteForRegionAsync(region, CancellationToken.None));
            }

            logger.LogInformation(
                "Champion analytics ingestion fan-out: enqueued {Count} per-region jobs.",
                enabledRegions.Count);
            return;
        }

        // Legacy single-region behavior
        await ExecuteForRegionInternalAsync(region: null, rampOnly: false, ct);
    }

    public async Task ExecuteRampAsync(CancellationToken ct = default)
    {
        var multiRegion = multiRegionOptions.Value;

        if (multiRegion.Enabled && multiRegion.Regions.Count > 0)
        {
            var enabledRegions = GetConfiguredEnabledRegions(multiRegion);
            foreach (var region in enabledRegions)
            {
                backgroundJobClient.Enqueue<ChampionAnalyticsIngestionJob>(
                    job => job.ExecuteForRegionRampAsync(region, CancellationToken.None));
            }

            logger.LogInformation(
                "Champion analytics ingestion ramp fan-out: enqueued {Count} per-region jobs.",
                enabledRegions.Count);
            return;
        }

        await ExecuteForRegionInternalAsync(region: null, rampOnly: true, ct);
    }

    [Queue("refresh-low")]
    public async Task ExecuteForRegionAsync(string region, CancellationToken ct = default)
    {
        await ExecuteForRegionInternalAsync(region, rampOnly: false, ct);
    }

    [Queue("refresh-low")]
    public async Task ExecuteForRegionRampAsync(string region, CancellationToken ct = default)
    {
        await ExecuteForRegionInternalAsync(region, rampOnly: true, ct);
    }

    private async Task ExecuteForRegionInternalAsync(string? region, bool rampOnly, CancellationToken ct = default)
    {
        var producerKey = region != null ? $"{ProducerKeyBase}:{region}" : ProducerKeyBase;
        var telemetrySource = region != null ? $"{TelemetrySource}:{region}" : TelemetrySource;
        var jobOptions = options.Value;
        var evaluationUtc = DateTime.UtcNow;
        var apiPriorityDemandActive = await refreshLockRepository.AnyActiveByPrefixAsync(
            RefreshLockKeys.ApiPriorityRefreshPrefix,
            ct);

        var hasTrackedSummoners = region != null
            ? await db.Summoners.AsNoTracking().AnyAsync(s => s.PlatformRegion == region, ct)
            : await db.Summoners.AsNoTracking().AnyAsync(ct);

        if (!hasTrackedSummoners)
            await bootstrapService.EnsureSeededFromChallengerAsync(ct);

        var currentPatchInfo = await db.Patches
            .AsNoTracking()
            .Where(p => p.IsActive)
            .Select(p => new { p.Version, p.ReleaseDate })
            .FirstOrDefaultAsync(ct);

        if (currentPatchInfo == null || string.IsNullOrWhiteSpace(currentPatchInfo.Version))
        {
            logger.LogWarning("Champion analytics ingestion skipped: no active patch found. region={Region}", region);
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
            logger.LogDebug("Champion analytics ingestion ramp run skipped: ramp window inactive. region={Region}", region);
            return;
        }

        var patchStartEpoch = new DateTimeOffset(currentPatchInfo.ReleaseDate, TimeSpan.Zero).ToUnixTimeSeconds();

        var minMatchesForPatch = Math.Max(1, jobOptions.MinimumSuccessfulMatchesForCurrentPatch);
        var targetMatchesForPatch = Math.Max(minMatchesForPatch, jobOptions.TargetSuccessfulMatchesForCurrentPatch);
        var staleAfterMinutes = Math.Max(5,
            isRampActive ? jobOptions.RampDataStaleAfterMinutes : jobOptions.DataStaleAfterMinutes);
        var staleCutoffUtc = evaluationUtc.AddMinutes(-staleAfterMinutes);

        // Region-scoped coverage queries
        var matchQuery = db.Matches.AsNoTracking()
            .Where(m => m.Status == FetchStatus.Success && m.Patch == currentPatch);
        if (region != null)
            matchQuery = matchQuery.Where(m => m.PlatformRegion == region);

        var successfulMatchesForPatch = await matchQuery.CountAsync(ct);

        var latestFetchAtUtc = await matchQuery
            .Where(m => m.FetchedAt != null)
            .MaxAsync(m => m.FetchedAt, ct);

        var recentSuccessWindowStartUtc = evaluationUtc.AddMinutes(-adaptiveThroughputBudgetPolicy.VelocityLookbackMinutes);
        var recentSuccessfulMatchesForPatch = await matchQuery
            .Where(m => m.FetchedAt != null && m.FetchedAt >= recentSuccessWindowStartUtc)
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

        var pendingCandidateCount = await EstimatePendingCandidateCountAsync(region, ct);
        var budget = adaptiveThroughputBudgetPolicy.ComputeBudget(new AdaptiveThroughputBudgetInput(
            producerKey,
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
        ingestionThroughputTelemetry.RecordBudgetDecision(
            producerKey,
            budget,
            apiPriorityDemandActive,
            telemetrySource);

        var guardrailDecision = await EvaluateStarvationGuardrailAsync(
            producerKey,
            region,
            evaluationUtc,
            budget.QueueTarget,
            budget.MaxCandidates,
            ct);
        ingestionThroughputTelemetry.RecordGuardrailDecision(
            producerKey,
            guardrailDecision,
            telemetrySource);
        var queuedTarget = guardrailDecision.QueueTarget;
        var maxCandidates = guardrailDecision.MaxCandidates;
        var forcedCatchUpActive = guardrailDecision.IsForcedCatchUpActive;
        var requiresColdStartProgress = region is not null &&
            successfulMatchesForPatch == 0 &&
            pendingCandidateCount > 0;

        if (requiresColdStartProgress)
        {
            queuedTarget = Math.Max(queuedTarget, 1);
            maxCandidates = Math.Max(maxCandidates, queuedTarget);
        }

        if (queuedTarget <= 0)
        {
            ingestionThroughputTelemetry.RecordQueueTargetOutput(
                producerKey,
                queuedTarget,
                queuedCount: 0,
                maxCandidates,
                budget.Mode,
                guardrailDecision.Outcome,
                forcedCatchUpActive,
                "skipped_zero_queue_target",
                telemetrySource);
            logger.LogInformation(
                "Champion analytics ingestion skipped: adaptive mode {Mode} and guardrail outcome {GuardrailOutcome} produced queue target {QueueTarget} (region={Region}, apiPriority={ApiPriority}, coverage={Coverage:F2}, backlogAgeMinutes={BacklogAge:F1}, velocityPerHour={Velocity:F2}, pressure={Pressure:F2}, deferAgeMinutes={DeferAge:F1}, deferThresholdMinutes={DeferThreshold:F1}).",
                budget.Mode,
                guardrailDecision.Outcome,
                queuedTarget,
                region,
                apiPriorityDemandActive,
                budget.CoverageRatio,
                budget.BacklogAgeMinutes,
                budget.RecentVelocityPerHour,
                budget.CandidatePressureRatio,
                guardrailDecision.MaxEligibleDeferAgeMinutes,
                guardrailDecision.DeferAgeThresholdMinutes);
            return;
        }

        if (jobOptions.PauseWhenApiPriorityRefreshActive &&
            apiPriorityDemandActive &&
            !forcedCatchUpActive &&
            !requiresColdStartProgress)
        {
            ingestionThroughputTelemetry.RecordQueueTargetOutput(
                producerKey,
                queuedTarget,
                queuedCount: 0,
                maxCandidates,
                budget.Mode,
                guardrailDecision.Outcome,
                forcedCatchUpActive,
                "skipped_api_priority_pause",
                telemetrySource);
            logger.LogInformation(
                "Champion analytics ingestion skipped: active high-priority API refresh demand detected while pause mode is enabled. region={Region}",
                region);
            return;
        }

        var lockTtl = TimeSpan.FromMinutes(Math.Max(2, jobOptions.RefreshLockMinutes));
        var includeAllModes = budget.IncludeAllModes;

        var candidates = await GetCandidatesAsync(region, maxCandidates, jobOptions, releaseUtc, evaluationUtc, ct);
        if (candidates.Count == 0)
        {
            ingestionThroughputTelemetry.RecordQueueTargetOutput(
                producerKey,
                queuedTarget,
                queuedCount: 0,
                maxCandidates,
                budget.Mode,
                guardrailDecision.Outcome,
                forcedCatchUpActive,
                "skipped_no_candidates",
                telemetrySource);
            logger.LogWarning(
                "Champion analytics ingestion skipped: no candidate summoners with Riot IDs are available. region={Region}",
                region);
            return;
        }

        var queued = 0;
        var preemptedByApiPriority = false;
        foreach (var candidate in candidates)
        {
            ct.ThrowIfCancellationRequested();
            if (queued >= queuedTarget) break;

            if (jobOptions.PauseWhenApiPriorityRefreshActive &&
                !forcedCatchUpActive &&
                !requiresColdStartProgress &&
                await refreshLockRepository.AnyActiveByPrefixAsync(RefreshLockKeys.ApiPriorityRefreshPrefix, ct))
            {
                logger.LogInformation(
                    "Champion analytics ingestion stopped early after queueing {QueuedCount}/{QueuedTarget} jobs due to active high-priority API refresh demand. region={Region}",
                    queued,
                    queuedTarget,
                    region);
                preemptedByApiPriority = true;
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
                    job.RefreshForAnalytics(candidate.GameName, candidate.TagLine, platform,
                        SummonerRefreshJob.BuildAnalyticsExecutionLockKey(lockKey, forcedCatchUpActive),
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

        var queueOutcome = preemptedByApiPriority
            ? "stopped_api_priority_preemption"
            : queued >= queuedTarget
                ? "queued_target_met"
                : "queued_target_partial";
        ingestionThroughputTelemetry.RecordQueueTargetOutput(
            producerKey,
            queuedTarget,
            queued,
            maxCandidates,
            budget.Mode,
            guardrailDecision.Outcome,
            forcedCatchUpActive,
            queueOutcome,
            telemetrySource);

        logger.LogInformation(
            "Champion analytics ingestion queued {QueuedCount}/{QueuedTarget} low-priority summoner refresh jobs (region={Region}, patch {Patch}, mode {Mode}, guardrail={GuardrailOutcome}, forceCatchUp={ForceCatchUp}, matches {MatchCount}/{TargetMatchCount}, stale {IsStale}, includeAllModes {IncludeAllModes}, latestFetchAt {LatestFetchAt}, ramp={Ramp}, coverage={Coverage:F2}, backlogAgeMinutes={BacklogAge:F1}, velocityPerHour={Velocity:F2}, pressure={Pressure:F2}, deferAgeMinutes={DeferAge:F1}, deferThresholdMinutes={DeferThreshold:F1}).",
            queued,
            queuedTarget,
            region,
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

    private List<string> GetConfiguredEnabledRegions(MultiRegionIngestionOptions multiRegion)
    {
        return multiRegion.Regions
            .Where(r => r.Enabled && !string.IsNullOrWhiteSpace(r.Region))
            .Select(r => r.Region.Trim().ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
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
        string? region,
        int maxCandidates,
        ChampionAnalyticsIngestionJobOptions jobOptions,
        DateTime patchReleaseUtc,
        DateTime evaluationUtc,
        CancellationToken ct)
    {
        var combined = new List<CandidateSummoner>();

        if (jobOptions.PrioritizeFavoriteSummoners)
        {
            var query = from s in db.Summoners.AsNoTracking()
                join f in db.UserFavoriteSummoners.AsNoTracking()
                    on new { Puuid = s.Puuid!, PlatformRegion = s.PlatformRegion! }
                    equals new { Puuid = f.SummonerPuuid, PlatformRegion = f.PlatformRegion }
                where s.GameName != null
                      && s.TagLine != null
                      && s.PlatformRegion != null
                select s;

            if (region != null)
                query = query.Where(s => s.PlatformRegion == region);

            var favoriteCandidates = await query
                .Select(s => new CandidateSummoner(
                    s.PlatformRegion!,
                    s.GameName!,
                    s.TagLine!,
                    s.UpdatedAt,
                    true))
                .ToListAsync(ct);

            combined.AddRange(favoriteCandidates);
        }

        if (jobOptions.FallbackToTrackedSummoners)
        {
            var fallbackQuery = db.Summoners
                .AsNoTracking()
                .Where(s => s.GameName != null && s.TagLine != null && s.PlatformRegion != null);

            if (region != null)
                fallbackQuery = fallbackQuery.Where(s => s.PlatformRegion == region);

            var fallbackCandidates = await fallbackQuery
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

    private Task<int> EstimatePendingCandidateCountAsync(string? region, CancellationToken ct)
    {
        var query = db.Summoners
            .AsNoTracking()
            .Where(s => s.GameName != null && s.TagLine != null && s.PlatformRegion != null);

        if (region != null)
            query = query.Where(s => s.PlatformRegion == region);

        return query.CountAsync(ct);
    }

    private async Task<StarvationGuardrailDecision> EvaluateStarvationGuardrailAsync(
        string producerKey,
        string? region,
        DateTime evaluationUtc,
        int baselineQueueTarget,
        int baselineMaxCandidates,
        CancellationToken ct)
    {
        var catchUpWindowKey = RefreshLockKeys.BuildStarvationGuardrailCatchUpKey(producerKey);
        var catchUpCooldownKey = RefreshLockKeys.BuildStarvationGuardrailCooldownKey(producerKey);
        var maxDeferAgeMinutes = await EstimateMaxEligibleDeferAgeMinutesAsync(region, evaluationUtc, ct);
        var telemetrySource = region != null ? $"{TelemetrySource}:{region}" : TelemetrySource;

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
        {
            if (decision.Outcome is StarvationGuardrailOutcome.CatchUpWindowContinue or StarvationGuardrailOutcome.CatchUpCooldown)
            {
                var lifecycleOutcome = decision.Outcome == StarvationGuardrailOutcome.CatchUpWindowContinue
                    ? "continue"
                    : "cooldown";
                ingestionThroughputTelemetry.RecordCatchUpWindowLifecycle(
                    producerKey,
                    lifecycleOutcome,
                    decision.CatchUpWindowTtl,
                    decision.CatchUpCooldownTtl,
                    telemetrySource);
            }

            return decision;
        }

        var catchUpStarted = await refreshLockRepository.TryAcquireAsync(catchUpWindowKey, decision.CatchUpWindowTtl, ct);
        if (catchUpStarted)
        {
            var cooldownTtl = decision.CatchUpWindowTtl + decision.CatchUpCooldownTtl;
            await refreshLockRepository.TryAcquireAsync(catchUpCooldownKey, cooldownTtl, ct);
            ingestionThroughputTelemetry.RecordCatchUpWindowLifecycle(
                producerKey,
                "started",
                decision.CatchUpWindowTtl,
                decision.CatchUpCooldownTtl,
                telemetrySource);
            return Evaluate(windowActive: true, cooldownActive: true);
        }

        ingestionThroughputTelemetry.RecordCatchUpWindowLifecycle(
            producerKey,
            "start_contention",
            decision.CatchUpWindowTtl,
            decision.CatchUpCooldownTtl,
            telemetrySource);

        catchUpWindowState = await refreshLockRepository.GetAsync(catchUpWindowKey, ct);
        catchUpCooldownState = await refreshLockRepository.GetAsync(catchUpCooldownKey, ct);
        return Evaluate(
            IsLockActive(catchUpWindowState, evaluationUtc),
            IsLockActive(catchUpCooldownState, evaluationUtc));
    }

    private async Task<double?> EstimateMaxEligibleDeferAgeMinutesAsync(string? region, DateTime evaluationUtc, CancellationToken ct)
    {
        var query = db.Summoners
            .AsNoTracking()
            .Where(s => s.GameName != null && s.TagLine != null && s.PlatformRegion != null);

        if (region != null)
            query = query.Where(s => s.PlatformRegion == region);

        var oldestUpdatedAt = await query
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
