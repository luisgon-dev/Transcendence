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
public class SummonerMaintenanceJob(
    TranscendenceContext db,
    IBackgroundJobClient backgroundJobClient,
    IRefreshLockRepository refreshLockRepository,
    IIngestionPriorityScoringPolicy scoringPolicy,
    IAdaptiveThroughputBudgetPolicy adaptiveThroughputBudgetPolicy,
    IStarvationGuardrailPolicy starvationGuardrailPolicy,
    IIngestionThroughputTelemetry ingestionThroughputTelemetry,
    IQueueDepthProbe queueDepthProbe,
    IOptions<SummonerMaintenanceJobOptions> options,
    IOptions<ChampionAnalyticsIngestionJobOptions> analyticsOptions,
    IOptions<MultiRegionIngestionOptions> multiRegionOptions,
    ILogger<SummonerMaintenanceJob> logger)
{
    private const string ProducerKeyBase = nameof(SummonerMaintenanceJob);
    private const string TelemetrySource = "summoner-maintenance-job";

    // Snowball-frontier marker: MatchService mints never-refreshed participant stubs with
    // UpdatedAt = DateTime.MinValue; any summoner older than this sentinel is an unrefreshed stub.
    private static readonly DateTime SnowballFrontierUpdatedAtCutoffUtc =
        new(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private sealed record CandidateSummoner(
        string PlatformRegion,
        string GameName,
        string TagLine,
        DateTime UpdatedAt,
        bool IsFavorite,
        bool IsTrackedHighValue,
        string? RankTier,
        bool IsSnowballFrontier,
        DateTime? LastActiveAtUtc);

    [Queue("refresh-low")]
    public async Task ExecuteAsync(CancellationToken ct = default)
    {
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
                backgroundJobClient.Enqueue<SummonerMaintenanceJob>(
                    job => job.ExecuteForRegionAsync(region, CancellationToken.None));
            }

            logger.LogInformation(
                "[Maintenance] Fan-out: enqueued {Count} per-region jobs.",
                enabledRegions.Count);
            return;
        }

        await ExecuteForRegionInternalAsync(region: null, ct);
    }

    [Queue(HangfireQueues.Discovery)]
    public async Task ExecuteForRegionAsync(string region, CancellationToken ct = default)
    {
        await ExecuteForRegionInternalAsync(region, ct);
    }

    // Acquires the producer's self-pacing slot. Returns false (skip) while a prior run's slot is still
    // held. TTL is the ramp interval within NewPatchRampHours of the active patch release, else the
    // steady interval — so cadence tightens automatically on a fresh patch and relaxes after.
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
                "[Maintenance] Paced-skip: within {Interval}m self-pace interval (ramp={Ramp}).",
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

        var activePatch = await db.Patches
            .AsNoTracking()
            .Where(p => p.IsActive)
            .Select(p => new { p.Version, p.ReleaseDate })
            .FirstOrDefaultAsync(ct);

        if (activePatch == null || string.IsNullOrWhiteSpace(activePatch.Version))
        {
            logger.LogWarning("[Maintenance] Skipped because no active patch exists. region={Region}", region);
            return;
        }

        var releaseUtc = activePatch.ReleaseDate.Kind == DateTimeKind.Utc
            ? activePatch.ReleaseDate
            : DateTime.SpecifyKind(activePatch.ReleaseDate, DateTimeKind.Utc);
        var rampHours = Math.Max(1, jobOptions.NewPatchRampHours);
        var isRampActive = evaluationUtc < releaseUtc.AddHours(rampHours);

        var patchStartEpoch = new DateTimeOffset(activePatch.ReleaseDate, TimeSpan.Zero).ToUnixTimeSeconds();

        // Region-scoped match queries
        var matchQuery = db.Matches.AsNoTracking()
            .Where(m => m.Status == FetchStatus.Success && m.Patch == activePatch.Version);
        if (region != null)
            matchQuery = matchQuery.Where(m => m.PlatformRegion == region);

        var successfulMatchesForPatch = await matchQuery.CountAsync(ct);

        var regionWeight = ResolveRegionWeight(region);
        var targetMatchesForPatch = Math.Max(
            ScaleTargetForRegion(analyticsOptions.Value.MinimumSuccessfulMatchesForCurrentPatch, regionWeight),
            ScaleTargetForRegion(analyticsOptions.Value.TargetSuccessfulMatchesForCurrentPatch, regionWeight));

        var latestFetchAtUtc = await matchQuery
            .Where(m => m.FetchedAt != null)
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
        var recentSuccessfulMatchesForPatch = await matchQuery
            .Where(m => m.FetchedAt != null && m.FetchedAt >= recentSuccessWindowStartUtc)
            .CountAsync(ct);
        var pendingCandidateCount = await EstimatePendingCandidateCountAsync(region, staleCutoffUtc, ct);

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
            staleCutoffUtc,
            budget.QueueTarget,
            budget.MaxCandidates,
            ct);
        ingestionThroughputTelemetry.RecordGuardrailDecision(
            producerKey,
            guardrailDecision,
            telemetrySource);
        var maxCandidates = guardrailDecision.MaxCandidates;
        var maxQueued = guardrailDecision.QueueTarget;
        var forcedCatchUpActive = guardrailDecision.IsForcedCatchUpActive;
        var requiresColdStartProgress = region is not null &&
            successfulMatchesForPatch == 0 &&
            pendingCandidateCount > 0;

        if (requiresColdStartProgress)
        {
            maxQueued = Math.Max(maxQueued, 1);
            maxCandidates = Math.Max(maxCandidates, maxQueued);
        }

        // Discovery-lane backpressure (final ceiling, overrides forced catch-up and cold-start): if the
        // discovery queue is already deep, the discovery workers are the bottleneck, so adding more
        // refresh jobs is waste and risks unbounded regrowth. Scale the target down by current depth.
        var discoveryQueueDepth = queueDepthProbe.GetEnqueuedCount(HangfireQueues.Discovery);
        var backpressuredQueued = QueueBackpressure.Apply(
            maxQueued,
            discoveryQueueDepth,
            analyticsOptions.Value.DiscoveryQueueBackpressureSoftCap,
            analyticsOptions.Value.DiscoveryQueueBackpressureHardCap);
        if (backpressuredQueued < maxQueued)
        {
            if (backpressuredQueued <= 0)
            {
                ingestionThroughputTelemetry.RecordQueueTargetOutput(
                    producerKey,
                    maxQueued,
                    queuedCount: 0,
                    maxCandidates,
                    budget.Mode,
                    guardrailDecision.Outcome,
                    forcedCatchUpActive,
                    "skipped_queue_backpressure",
                    telemetrySource);
                logger.LogInformation(
                    "[Maintenance] Skipped: discovery queue depth {Depth} at/above backpressure hard cap {HardCap} (region={Region}). Waiting for workers to drain.",
                    discoveryQueueDepth,
                    analyticsOptions.Value.DiscoveryQueueBackpressureHardCap,
                    region);
                return;
            }

            logger.LogInformation(
                "[Maintenance] Throttled by discovery backpressure: queue target {Original} -> {Throttled} (depth {Depth}, soft {Soft}, hard {Hard}, region={Region}).",
                maxQueued,
                backpressuredQueued,
                discoveryQueueDepth,
                analyticsOptions.Value.DiscoveryQueueBackpressureSoftCap,
                analyticsOptions.Value.DiscoveryQueueBackpressureHardCap,
                region);
            maxQueued = backpressuredQueued;
        }

        if (maxQueued <= 0)
        {
            ingestionThroughputTelemetry.RecordQueueTargetOutput(
                producerKey,
                maxQueued,
                queuedCount: 0,
                maxCandidates,
                budget.Mode,
                guardrailDecision.Outcome,
                forcedCatchUpActive,
                "skipped_zero_queue_target",
                telemetrySource);
            logger.LogInformation(
                "[Maintenance] Skipped because adaptive mode {Mode} and guardrail outcome {GuardrailOutcome} produced queue target {QueueTarget} (region={Region}, apiPriority={ApiPriority}, coverage={Coverage:F2}, backlogAgeMinutes={BacklogAge:F1}, velocityPerHour={Velocity:F2}, pressure={Pressure:F2}, deferAgeMinutes={DeferAge:F1}, deferThresholdMinutes={DeferThreshold:F1}).",
                budget.Mode,
                guardrailDecision.Outcome,
                maxQueued,
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
                maxQueued,
                queuedCount: 0,
                maxCandidates,
                budget.Mode,
                guardrailDecision.Outcome,
                forcedCatchUpActive,
                "skipped_api_priority_pause",
                telemetrySource);
            logger.LogInformation("[Maintenance] Skipped due to active high-priority API refresh demand. region={Region}", region);
            return;
        }

        var includeAllModes = budget.IncludeAllModes;
        var lockTtl = TimeSpan.FromMinutes(Math.Max(2, jobOptions.RefreshLockMinutes));

        var candidates = await GetCandidatesAsync(region, staleCutoffUtc, maxCandidates, releaseUtc, evaluationUtc, jobOptions, ct);
        if (candidates.Count == 0)
        {
            ingestionThroughputTelemetry.RecordQueueTargetOutput(
                producerKey,
                maxQueued,
                queuedCount: 0,
                maxCandidates,
                budget.Mode,
                guardrailDecision.Outcome,
                forcedCatchUpActive,
                "skipped_no_candidates",
                telemetrySource);
            logger.LogInformation("[Maintenance] No stale summoner candidates were eligible. region={Region}", region);
            return;
        }

        var queued = 0;
        var preemptedByApiPriority = false;
        foreach (var candidate in candidates)
        {
            ct.ThrowIfCancellationRequested();
            if (queued >= maxQueued)
                break;

            if (jobOptions.PauseWhenApiPriorityRefreshActive &&
                !forcedCatchUpActive &&
                !requiresColdStartProgress &&
                await refreshLockRepository.AnyActiveByPrefixAsync(RefreshLockKeys.ApiPriorityRefreshPrefix, ct))
            {
                logger.LogInformation(
                    "[Maintenance] Stopped early after queueing {QueuedCount}/{QueuedTarget} jobs due to active high-priority API refresh demand. region={Region}",
                    queued,
                    maxQueued,
                    region);
                preemptedByApiPriority = true;
                break;
            }

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
                        SummonerRefreshJob.BuildAnalyticsExecutionLockKey(lockKey, forcedCatchUpActive),
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

        var queueOutcome = preemptedByApiPriority
            ? "stopped_api_priority_preemption"
            : queued >= maxQueued
                ? "queued_target_met"
                : "queued_target_partial";
        ingestionThroughputTelemetry.RecordQueueTargetOutput(
            producerKey,
            maxQueued,
            queued,
            maxCandidates,
            budget.Mode,
            guardrailDecision.Outcome,
            forcedCatchUpActive,
            queueOutcome,
            telemetrySource);

        logger.LogInformation(
            "[Maintenance] Queued {Queued}/{Target} refresh jobs. region={Region}, includeAllModes={IncludeAllModes}, patch={Patch}, mode={Mode}, guardrail={GuardrailOutcome}, forceCatchUp={ForceCatchUp}, coverage={Coverage}, ramp={Ramp}, backlogAgeMinutes={BacklogAge:F1}, velocityPerHour={Velocity:F2}, pressure={Pressure:F2}, deferAgeMinutes={DeferAge:F1}, deferThresholdMinutes={DeferThreshold:F1}.",
            queued,
            maxQueued,
            region,
            includeAllModes,
            activePatch.Version,
            budget.Mode,
            guardrailDecision.Outcome,
            forcedCatchUpActive,
            successfulMatchesForPatch,
            isRampActive,
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

    private async Task<List<CandidateSummoner>> GetCandidatesAsync(
        string? region,
        DateTime staleCutoffUtc,
        int maxCandidates,
        DateTime patchReleaseUtc,
        DateTime evaluationUtc,
        SummonerMaintenanceJobOptions options,
        CancellationToken ct)
    {
        var combined = new List<CandidateSummoner>();
        var highEloTiers = options.HighEloTiers
            .Where(tier => !string.IsNullOrWhiteSpace(tier))
            .Select(tier => tier.Trim().ToUpperInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        // Snowball frontier: never-refreshed stubs minted from freshly-ingested match participants
        // (UpdatedAt = MinValue) — guaranteed-active and uncovered. Ranked into their own high bucket.
        if (options.PrioritizeSnowballFrontier)
        {
            var frontierQuery = db.Summoners.AsNoTracking()
                .Where(s => s.GameName != null && s.TagLine != null && s.PlatformRegion != null)
                .Where(s => s.UpdatedAt <= SnowballFrontierUpdatedAtCutoffUtc);

            if (region != null)
                frontierQuery = frontierQuery.Where(s => s.PlatformRegion == region);

            var frontierCandidates = await frontierQuery
                .OrderBy(s => s.UpdatedAt)
                .Take(maxCandidates * 3)
                .Select(s => new CandidateSummoner(
                    s.PlatformRegion!,
                    s.GameName!,
                    s.TagLine!,
                    s.UpdatedAt,
                    IsFavorite: false,
                    IsTrackedHighValue: false,
                    RankTier: null,
                    IsSnowballFrontier: true,
                    s.LastActiveAtUtc))
                .ToListAsync(ct);

            combined.AddRange(frontierCandidates);
        }

        if (options.PrioritizeTrackedHighValueSummoners)
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
                let effectiveUpdatedAt = summoner != null ? summoner.UpdatedAt : tracked.UpdatedAtUtc
                where effectiveUpdatedAt <= staleCutoffUtc
                select new
                {
                    tracked.PlatformRegion,
                    tracked.GameName,
                    tracked.TagLine,
                    UpdatedAt = effectiveUpdatedAt,
                    LastActiveAtUtc = summoner != null ? summoner.LastActiveAtUtc : (DateTime?)null,
                    RankTier = rank != null ? rank.Tier : null
                };

            if (region != null)
                trackedQuery = trackedQuery.Where(x => x.PlatformRegion == region);

            var trackedHighValueCandidates = await trackedQuery.ToListAsync(ct);

            combined.AddRange(trackedHighValueCandidates.Select(x => new CandidateSummoner(
                x.PlatformRegion!,
                x.GameName!,
                x.TagLine!,
                x.UpdatedAt,
                IsFavorite: false,
                IsTrackedHighValue: true,
                x.RankTier,
                IsSnowballFrontier: false,
                x.LastActiveAtUtc)));
        }

        if (options.PrioritizeFavoriteSummoners)
        {
            var query = from s in db.Summoners.AsNoTracking()
                join f in db.UserFavoriteSummoners.AsNoTracking()
                    on new { Puuid = s.Puuid!, PlatformRegion = s.PlatformRegion! }
                    equals new { Puuid = f.SummonerPuuid, PlatformRegion = f.PlatformRegion }
                where s.GameName != null
                      && s.TagLine != null
                      && s.PlatformRegion != null
                      && s.UpdatedAt <= staleCutoffUtc
                select s;

            if (region != null)
                query = query.Where(s => s.PlatformRegion == region);

            var favoriteCandidates = await query
                .Select(s => new CandidateSummoner(
                    s.PlatformRegion!,
                    s.GameName!,
                    s.TagLine!,
                    s.UpdatedAt,
                    IsFavorite: true,
                    IsTrackedHighValue: false,
                    RankTier: null,
                    IsSnowballFrontier: false,
                    s.LastActiveAtUtc))
                .ToListAsync(ct);

            combined.AddRange(favoriteCandidates);
        }

        if (options.PrioritizeRankedHighEloSummoners && highEloTiers.Count > 0)
        {
            var highEloQuery =
                from s in db.Summoners.AsNoTracking()
                join r in db.Ranks.AsNoTracking().Where(r => r.QueueType == "RANKED_SOLO_5x5")
                    on s.Id equals r.SummonerId
                where s.GameName != null
                      && s.TagLine != null
                      && s.PlatformRegion != null
                      && s.UpdatedAt <= staleCutoffUtc
                      && highEloTiers.Contains((r.Tier ?? string.Empty).ToUpper())
                select new
                {
                    s.PlatformRegion,
                    s.GameName,
                    s.TagLine,
                    s.UpdatedAt,
                    s.LastActiveAtUtc,
                    RankTier = r.Tier
                };

            if (region != null)
                highEloQuery = highEloQuery.Where(x => x.PlatformRegion == region);

            var highEloCandidates = await highEloQuery.ToListAsync(ct);

            combined.AddRange(highEloCandidates.Select(x => new CandidateSummoner(
                x.PlatformRegion!,
                x.GameName!,
                x.TagLine!,
                x.UpdatedAt,
                IsFavorite: false,
                IsTrackedHighValue: false,
                x.RankTier,
                IsSnowballFrontier: false,
                x.LastActiveAtUtc)));
        }

        // Long tail: the stale pool excluding the frontier (handled above), with a bounded random
        // starting offset so successive runs spread across the pool instead of re-picking the same head.
        var trackedSummonerQuery = db.Summoners
            .AsNoTracking()
            .Where(s => s.GameName != null && s.TagLine != null && s.PlatformRegion != null)
            .Where(s => s.UpdatedAt > SnowballFrontierUpdatedAtCutoffUtc && s.UpdatedAt <= staleCutoffUtc);

        if (region != null)
            trackedSummonerQuery = trackedSummonerQuery.Where(s => s.PlatformRegion == region);

        var take = maxCandidates * 3;
        var offset = await ComputeFallbackRotationOffsetAsync(trackedSummonerQuery, take, options.FallbackRotationMaxOffset, ct);

        var trackedCandidates = await trackedSummonerQuery
            .OrderBy(s => s.UpdatedAt)
            .Skip(offset)
            .Take(take)
            .Select(s => new CandidateSummoner(
                s.PlatformRegion!,
                s.GameName!,
                s.TagLine!,
                s.UpdatedAt,
                IsFavorite: false,
                IsTrackedHighValue: false,
                RankTier: null,
                IsSnowballFrontier: false,
                s.LastActiveAtUtc))
            .ToListAsync(ct);

        combined.AddRange(trackedCandidates);

        var rankedCandidates = RankCandidates(combined, patchReleaseUtc, evaluationUtc, maxCandidates);

        return rankedCandidates.ToList();
    }

    // Bounded random offset for the long-tail fallback so runs walk different slices of the stale pool;
    // capped to keep the Skip cheap on the (PlatformRegion, UpdatedAt) index and never past (pool - take).
    private static async Task<int> ComputeFallbackRotationOffsetAsync(
        IQueryable<Summoner> fallbackPool,
        int take,
        int maxOffset,
        CancellationToken ct)
    {
        if (maxOffset <= 0)
            return 0;

        var poolCount = await fallbackPool.CountAsync(ct);
        var rotatable = poolCount - Math.Max(0, take);
        if (rotatable <= 0)
            return 0;

        var upperBound = Math.Min(rotatable, maxOffset);
        return Random.Shared.Next(0, upperBound + 1);
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
                        candidate.IsFavorite)
                    {
                        LastActiveAtUtc = candidate.LastActiveAtUtc
                    },
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

    // Bucket order (lower = higher priority): tracked pros, snowball frontier, ranked high-elo,
    // favorites, long-tail fallback. The frontier sits above high-elo so the productive uncovered set
    // is reached before re-refreshing the (cooldown-filtered) covered pool.
    private static int GetPriorityBucket(CandidateSummoner candidate)
    {
        if (candidate.IsTrackedHighValue)
            return 0;

        if (candidate.IsSnowballFrontier)
            return 1;

        if (GetTierPriority(candidate.RankTier) < 5)
            return 2;

        if (candidate.IsFavorite)
            return 3;

        return 4;
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

    private Task<int> EstimatePendingCandidateCountAsync(string? region, DateTime staleCutoffUtc, CancellationToken ct)
    {
        var query = db.Summoners
            .AsNoTracking()
            .Where(s => s.GameName != null && s.TagLine != null && s.PlatformRegion != null)
            .Where(s => s.UpdatedAt <= staleCutoffUtc);

        if (region != null)
            query = query.Where(s => s.PlatformRegion == region);

        return query.CountAsync(ct);
    }

    private async Task<StarvationGuardrailDecision> EvaluateStarvationGuardrailAsync(
        string producerKey,
        string? region,
        DateTime evaluationUtc,
        DateTime staleCutoffUtc,
        int baselineQueueTarget,
        int baselineMaxCandidates,
        CancellationToken ct)
    {
        var catchUpWindowKey = RefreshLockKeys.BuildStarvationGuardrailCatchUpKey(producerKey);
        var catchUpCooldownKey = RefreshLockKeys.BuildStarvationGuardrailCooldownKey(producerKey);
        var maxDeferAgeMinutes = await EstimateMaxEligibleDeferAgeMinutesAsync(region, evaluationUtc, staleCutoffUtc, ct);
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

    private async Task<double?> EstimateMaxEligibleDeferAgeMinutesAsync(
        string? region,
        DateTime evaluationUtc,
        DateTime staleCutoffUtc,
        CancellationToken ct)
    {
        var query = db.Summoners
            .AsNoTracking()
            .Where(s => s.GameName != null && s.TagLine != null && s.PlatformRegion != null)
            .Where(s => s.UpdatedAt <= staleCutoffUtc);

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
