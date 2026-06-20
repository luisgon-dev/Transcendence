using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Transcendence.Data;
using Transcendence.Data.Models.LoL.Account;
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
    IQueueDepthProbe queueDepthProbe,
    IOptions<ChampionAnalyticsIngestionJobOptions> options,
    IOptions<MultiRegionIngestionOptions> multiRegionOptions,
    IWorkerHeartbeat workerHeartbeat,
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
        bool IsFavorite,
        bool IsTrackedHighValue,
        string? RankTier);

    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        // Liveness beat for the worker watchdog — recorded on every dispatcher fire, before the
        // pacing gate, so a hung worker (no dispatcher fires at all) goes stale even in steady state.
        await workerHeartbeat.BeatAsync(ct);

        // Self-pacing: one fast heartbeat cron fires this dispatcher; the pacing slot decides whether
        // this tick actually fans out (tighter cadence during a new-patch ramp, looser in steady state).
        if (!await TryAcquirePacingSlotAsync(ct))
            return;

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
        await ExecuteForRegionInternalAsync(region: null, ct);
    }

    [Queue(HangfireQueues.Discovery)]
    public async Task ExecuteForRegionAsync(string region, CancellationToken ct = default)
    {
        await ExecuteForRegionInternalAsync(region, ct);
    }

    // Acquires the producer's self-pacing slot. Returns false (skip) while a prior run's slot is still
    // held. The TTL is the ramp interval within NewPatchRampHours of the active patch release, else the
    // steady interval — so the effective cadence tightens automatically on a fresh patch and relaxes after.
    private async Task<bool> TryAcquirePacingSlotAsync(CancellationToken ct)
    {
        var jobOptions = options.Value;
        var activePatch = await db.Patches
            .AsNoTracking()
            .Where(p => p.IsActive)
            .Select(p => new { p.ReleaseDate })
            .FirstOrDefaultAsync(ct);

        var isRampActive = false;
        if (activePatch != null)
        {
            var releaseUtc = activePatch.ReleaseDate.Kind == DateTimeKind.Utc
                ? activePatch.ReleaseDate
                : DateTime.SpecifyKind(activePatch.ReleaseDate, DateTimeKind.Utc);
            var rampHours = Math.Max(1, jobOptions.NewPatchRampHours);
            isRampActive = DateTime.UtcNow < releaseUtc.AddHours(rampHours);
        }

        var intervalMinutes = Math.Max(1, isRampActive
            ? jobOptions.SelfPaceRampIntervalMinutes
            : jobOptions.SelfPaceSteadyIntervalMinutes);
        // Expire ~30s before the next same-interval heartbeat so the next eligible tick reliably acquires.
        var ttl = TimeSpan.FromSeconds(Math.Max(30, intervalMinutes * 60 - 30));
        var pacingKey = RefreshLockKeys.BuildProducerPacingKey(ProducerKeyBase);

        var acquired = await refreshLockRepository.TryAcquireAsync(pacingKey, ttl, ct);
        if (!acquired)
        {
            logger.LogDebug(
                "Champion analytics ingestion paced-skip: within {Interval}m self-pace interval (ramp={Ramp}).",
                intervalMinutes,
                isRampActive);
        }

        return acquired;
    }

    private async Task ExecuteForRegionInternalAsync(string? region, CancellationToken ct = default)
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
        {
            if (region != null)
                await bootstrapService.EnsureSeededForRegionAsync(region, ct);
            else
                await bootstrapService.EnsureSeededFromChallengerAsync(ct);
        }

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

        var patchStartEpoch = new DateTimeOffset(currentPatchInfo.ReleaseDate, TimeSpan.Zero).ToUnixTimeSeconds();

        var regionWeight = ResolveRegionWeight(region);
        var minMatchesForPatch = ScaleTargetForRegion(jobOptions.MinimumSuccessfulMatchesForCurrentPatch, regionWeight);
        var targetMatchesForPatch = Math.Max(
            minMatchesForPatch,
            ScaleTargetForRegion(jobOptions.TargetSuccessfulMatchesForCurrentPatch, regionWeight));
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

        // Discovery-lane backpressure (final ceiling, overrides forced catch-up and cold-start): if the
        // discovery queue is already deep, the 10 discovery workers are the bottleneck, so adding more
        // refresh consumers is waste and risks unbounded regrowth. Scale the target down by current depth.
        var discoveryQueueDepth = queueDepthProbe.GetEnqueuedCount(HangfireQueues.Discovery);
        var backpressuredTarget = QueueBackpressure.Apply(
            queuedTarget,
            discoveryQueueDepth,
            jobOptions.DiscoveryQueueBackpressureSoftCap,
            jobOptions.DiscoveryQueueBackpressureHardCap);
        if (backpressuredTarget < queuedTarget)
        {
            if (backpressuredTarget <= 0)
            {
                ingestionThroughputTelemetry.RecordQueueTargetOutput(
                    producerKey,
                    queuedTarget,
                    queuedCount: 0,
                    maxCandidates,
                    budget.Mode,
                    guardrailDecision.Outcome,
                    forcedCatchUpActive,
                    "skipped_queue_backpressure",
                    telemetrySource);
                logger.LogInformation(
                    "Champion analytics ingestion skipped: discovery queue depth {Depth} at/above backpressure hard cap {HardCap} (region={Region}). Waiting for workers to drain.",
                    discoveryQueueDepth,
                    jobOptions.DiscoveryQueueBackpressureHardCap,
                    region);
                return;
            }

            logger.LogInformation(
                "Champion analytics ingestion throttled by discovery backpressure: queue target {Original} -> {Throttled} (depth {Depth}, soft {Soft}, hard {Hard}, region={Region}).",
                queuedTarget,
                backpressuredTarget,
                discoveryQueueDepth,
                jobOptions.DiscoveryQueueBackpressureSoftCap,
                jobOptions.DiscoveryQueueBackpressureHardCap,
                region);
            queuedTarget = backpressuredTarget;
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
        var includeAllModes = false;

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

    private double ResolveRegionWeight(string? region)
    {
        if (string.IsNullOrWhiteSpace(region))
            return 1d;

        var configuredWeight = multiRegionOptions.Value.Regions
            .Where(r => r.Enabled && !string.IsNullOrWhiteSpace(r.Region))
            .FirstOrDefault(r => string.Equals(r.Region.Trim(), region, StringComparison.OrdinalIgnoreCase))
            ?.Weight;

        return configuredWeight is > 0d ? configuredWeight.Value : 1d;
    }

    private static int ScaleTargetForRegion(int baselineTarget, double regionWeight)
    {
        var safeTarget = Math.Max(1, baselineTarget);
        var safeWeight = regionWeight > 0d ? regionWeight : 1d;
        return Math.Max(1, (int)Math.Ceiling(safeTarget * safeWeight));
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
        var highEloTiers = jobOptions.HighEloTiers
            .Where(tier => !string.IsNullOrWhiteSpace(tier))
            .Select(tier => tier.Trim().ToUpperInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (jobOptions.PrioritizeTrackedHighValueSummoners)
        {
            var trackedQuery =
                from tracked in db.TrackedProSummoners.AsNoTracking()
                where tracked.IsActive
                      && tracked.GameName != null
                      && tracked.TagLine != null
                      && tracked.PlatformRegion != null
                join summoner in db.Summoners.AsNoTracking()
                    on new { tracked.Puuid, tracked.PlatformRegion }
                    equals new { summoner.Puuid, summoner.PlatformRegion } into summonerGroup
                from summoner in summonerGroup.DefaultIfEmpty()
                join rank in db.Ranks.AsNoTracking().Where(r => r.QueueType == "RANKED_SOLO_5x5")
                    on summoner.Id equals rank.SummonerId into rankGroup
                from rank in rankGroup.DefaultIfEmpty()
                select new
                {
                    tracked.PlatformRegion,
                    tracked.GameName,
                    tracked.TagLine,
                    UpdatedAt = summoner != null ? summoner.UpdatedAt : tracked.UpdatedAtUtc,
                    RankTier = rank != null ? rank.Tier : null
                };

            if (region != null)
                trackedQuery = trackedQuery.Where(x => x.PlatformRegion == region);

            // Bound the high-value sub-queries: only the top `maxCandidates` (most stale) are ever
            // selected after ranking, so materializing the entire tracked/favorite/high-elo rosters
            // (which grow unbounded across regions) is pure waste.
            var trackedCandidates = await trackedQuery
                .OrderBy(x => x.UpdatedAt)
                .Take(maxCandidates)
                .ToListAsync(ct);

            combined.AddRange(trackedCandidates.Select(x => new CandidateSummoner(
                x.PlatformRegion!,
                x.GameName!,
                x.TagLine!,
                x.UpdatedAt,
                IsFavorite: false,
                IsTrackedHighValue: true,
                x.RankTier)));
        }

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
                .OrderBy(s => s.UpdatedAt)
                .Take(maxCandidates)
                .Select(s => new CandidateSummoner(
                    s.PlatformRegion!,
                    s.GameName!,
                    s.TagLine!,
                    s.UpdatedAt,
                    IsFavorite: true,
                    IsTrackedHighValue: false,
                    RankTier: null))
                .ToListAsync(ct);

            combined.AddRange(favoriteCandidates);
        }

        if (jobOptions.PrioritizeRankedHighEloSummoners && highEloTiers.Count > 0)
        {
            var highEloQuery =
                from s in db.Summoners.AsNoTracking()
                join r in db.Ranks.AsNoTracking().Where(r => r.QueueType == "RANKED_SOLO_5x5")
                    on s.Id equals r.SummonerId
                where s.GameName != null
                      && s.TagLine != null
                      && s.PlatformRegion != null
                      && highEloTiers.Contains((r.Tier ?? string.Empty).ToUpper())
                select new
                {
                    s.PlatformRegion,
                    s.GameName,
                    s.TagLine,
                    s.UpdatedAt,
                    RankTier = r.Tier
                };

            if (region != null)
                highEloQuery = highEloQuery.Where(x => x.PlatformRegion == region);

            var highEloCandidates = await highEloQuery
                .OrderBy(x => x.UpdatedAt)
                .Take(maxCandidates)
                .Select(x => new
                {
                    x.PlatformRegion,
                    x.GameName,
                    x.TagLine,
                    x.UpdatedAt,
                    x.RankTier
                })
                .ToListAsync(ct);

            combined.AddRange(highEloCandidates.Select(x => new CandidateSummoner(
                x.PlatformRegion!,
                x.GameName!,
                x.TagLine!,
                x.UpdatedAt,
                IsFavorite: false,
                IsTrackedHighValue: false,
                x.RankTier)));
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
                    IsFavorite: false,
                    IsTrackedHighValue: false,
                    RankTier: null))
                .ToListAsync(ct);

            combined.AddRange(fallbackCandidates);
        }

        var rankedCandidates = RankCandidates(combined, patchReleaseUtc, evaluationUtc, maxCandidates);

        return rankedCandidates.ToList();
    }

    private IReadOnlyList<CandidateSummoner> RankCandidates(
        IEnumerable<CandidateSummoner> candidates,
        DateTime patchReleaseUtc,
        DateTime evaluationUtc,
        int maxCandidates)
    {
        var boundedMax = Math.Max(1, maxCandidates);

        return candidates
            .Select(candidate => new
            {
                Candidate = candidate,
                Identity = RefreshLockKeys.BuildCanonicalIdentity(
                    candidate.PlatformRegion,
                    candidate.GameName,
                    candidate.TagLine),
                Bucket = GetPriorityBucket(candidate),
                TierPriority = GetTierPriority(candidate.RankTier),
                Score = scoringPolicy.ComputeScore(
                    new IngestionPriorityCandidate(
                        RefreshLockKeys.BuildCanonicalIdentity(
                            candidate.PlatformRegion,
                            candidate.GameName,
                            candidate.TagLine),
                        candidate.UpdatedAt,
                        candidate.IsFavorite),
                    new IngestionPriorityContext(patchReleaseUtc, evaluationUtc))
            })
            .OrderBy(x => x.Bucket)
            .ThenBy(x => x.TierPriority)
            .ThenByDescending(x => x.Score)
            .ThenBy(x => x.Candidate.UpdatedAt)
            .ThenBy(x => x.Identity, StringComparer.Ordinal)
            .DistinctBy(x => x.Identity, StringComparer.Ordinal)
            .Take(boundedMax)
            .Select(x => x.Candidate)
            .ToList();
    }

    private static int GetPriorityBucket(CandidateSummoner candidate)
    {
        if (candidate.IsTrackedHighValue)
            return 0;

        if (GetTierPriority(candidate.RankTier) < 5)
            return 1;

        if (candidate.IsFavorite)
            return 2;

        return 3;
    }

    private static int GetTierPriority(string? rankTier)
    {
        var normalizedTier = rankTier?.Trim().ToUpperInvariant();
        return normalizedTier switch
        {
            "CHALLENGER" => 0,
            "GRANDMASTER" => 1,
            "MASTER" => 2,
            "DIAMOND" => 3,
            "EMERALD" => 4,
            _ => 5
        };
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
        // Skip the full MIN(UpdatedAt) scan over Summoners when the guardrail is disabled — its
        // Evaluate() returns Disabled without using the value, so the scan would be pure wasted
        // load (per region × per producer × heartbeat).
        var maxDeferAgeMinutes = starvationGuardrailPolicy.Enabled
            ? await EstimateMaxEligibleDeferAgeMinutesAsync(region, evaluationUtc, ct)
            : (double?)null;
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
