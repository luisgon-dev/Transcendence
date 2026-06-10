using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.ComponentModel.DataAnnotations;
using Hangfire;
using Hangfire.Common;
using Hangfire.Storage;
using Hangfire.Storage.Monitoring;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Transcendence.Data;
using Transcendence.Data.Models.LoL.Match;
using Transcendence.Service.Core.Services.Analytics.Interfaces;
using Transcendence.Service.Core.Services.Auth.Interfaces;
using Transcendence.Service.Core.Services.Auth.Models;
using Transcendence.Service.Core.Services.Diagnostics;
using Transcendence.Service.Core.Services.Jobs.Configuration;
using Transcendence.Service.Core.Services.RiotApi;
using Transcendence.WebAPI.Security;

namespace Transcendence.WebAPI.Controllers;

[ApiController]
[Route("api/admin")]
[Authorize(Policy = AuthPolicies.AdminOnly)]
public class AdminOperationsController(
    JobStorage jobStorage,
    IConfiguration configuration,
    IOptions<WorkerJobScheduleOptions> workerScheduleOptions,
    IOptions<ChampionAnalyticsIngestionJobOptions> ingestionOptions,
    IOptions<MultiRegionIngestionOptions> multiRegionOptions,
    IRecurringJobManager recurringJobManager,
    IWorkerRecurringJobPolicy recurringJobPolicy,
    TranscendenceContext db,
    IChampionAnalyticsService analyticsService,
    IAdminAuditService adminAuditService) : ControllerBase
{
    private const int DefaultJobScanLimit = 5000;
    private const int DefaultJobScanPageSize = 250;
    private static readonly HashSet<string> AllowedServiceLogKeys = ["webapi", "service"];
    private static readonly HashSet<string> BulkDeletableStates = ["enqueued", "scheduled", "failed"];
    private static readonly HashSet<string> SupportedListStates = ["enqueued", "processing", "scheduled", "failed"];
    private static readonly HashSet<string> PausableRecurringJobIds =
    [
        WorkerRecurringJobPolicy.ChampionAnalyticsIngestionJobId,
        WorkerRecurringJobPolicy.SummonerMaintenanceJobId,
        WorkerRecurringJobPolicy.MatchTimelineBackfillJobId,
        WorkerRecurringJobPolicy.RetryFailedMatchesJobId
    ];

    private static readonly string[] KnownPlatformRegions =
    [
        "NA1", "EUW1", "EUN1", "KR", "BR1", "JP1", "LA1", "LA2", "OC1", "TR1", "RU", "PH2", "SG2", "TH2", "TW2",
        "VN2"
    ];

    [HttpGet("overview")]
    [ProducesResponseType(typeof(AdminOverviewResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetOverview(CancellationToken ct)
    {
        var monitoring = jobStorage.GetMonitoringApi();
        var stats = monitoring.GetStatistics();
        var queues = monitoring.Queues()
            .Select(q => new AdminQueueSnapshot(
                q.Name,
                q.Length,
                q.Fetched))
            .OrderByDescending(x => x.Length)
            .ToList();
        var servers = monitoring.Servers()
            .Select(s => new AdminServerSnapshot(
                s.Name,
                s.WorkersCount,
                s.StartedAt,
                s.Heartbeat,
                s.Queues.ToList()))
            .OrderBy(x => x.Name, StringComparer.Ordinal)
            .ToList();

        var dbConnected = await db.Database.CanConnectAsync(ct);
        return Ok(new AdminOverviewResponse(
            DateTime.UtcNow,
            dbConnected,
            stats.Enqueued,
            stats.Processing,
            stats.Scheduled,
            stats.Failed,
            stats.Succeeded,
            stats.Recurring,
            stats.Deleted,
            servers.Sum(x => x.WorkersCount),
            servers,
            queues));
    }

    [HttpGet("metrics/analysis")]
    [ProducesResponseType(typeof(AdminAnalysisMetricsResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAnalysisMetrics(CancellationToken ct)
    {
        var generatedAtUtc = DateTime.UtcNow;
        var monitoring = jobStorage.GetMonitoringApi();
        var backlogByRegion = BuildBacklogByRegion(
            ScanJobs(monitoring, ["enqueued", "processing", "scheduled"], scanLimit: 2500, out _));

        var enabledRegions = GetEnabledRegions();
        var activePatch = await db.Patches
            .AsNoTracking()
            .Where(p => p.IsActive)
            .Select(p => new { p.Version, p.ReleaseDate })
            .FirstOrDefaultAsync(ct);

        var activePatchVersion = activePatch?.Version;
        var staleAfterMinutes = Math.Max(15, ingestionOptions.Value.DataStaleAfterMinutes);
        var staleCutoffUtc = generatedAtUtc.AddMinutes(-staleAfterMinutes);

        var summonerCounts = (await db.Summoners
                .AsNoTracking()
                .GroupBy(x => x.PlatformRegion)
                .Select(g => new { Region = g.Key, Count = g.LongCount() })
                .ToListAsync(ct))
            .ToDictionary(x => NormalizeRegionKey(x.Region), x => x.Count, StringComparer.OrdinalIgnoreCase);

        var proCounts = (await db.TrackedProSummoners
                .AsNoTracking()
                .Where(x => x.IsActive)
                .GroupBy(x => x.PlatformRegion)
                .Select(g => new { Region = g.Key, Count = g.LongCount() })
                .ToListAsync(ct))
            .ToDictionary(x => NormalizeRegionKey(x.Region), x => x.Count, StringComparer.OrdinalIgnoreCase);

        Dictionary<string, RegionMatchAggregate> matchAggregates;
        Dictionary<string, RegionTimelineAggregate> timelineAggregates;
        long totalMatches;
        long currentPatchSuccessfulMatches;
        long currentPatchRankedMatches;
        long currentPatchTimelineSuccess;
        long currentPatchTimelineEligible;

        if (string.IsNullOrWhiteSpace(activePatchVersion))
        {
            matchAggregates = new Dictionary<string, RegionMatchAggregate>(StringComparer.OrdinalIgnoreCase);
            timelineAggregates = new Dictionary<string, RegionTimelineAggregate>(StringComparer.OrdinalIgnoreCase);
            totalMatches = await db.Matches.IgnoreQueryFilters().AsNoTracking().LongCountAsync(ct);
            currentPatchSuccessfulMatches = 0;
            currentPatchRankedMatches = 0;
            currentPatchTimelineSuccess = 0;
            currentPatchTimelineEligible = 0;
        }
        else
        {
            totalMatches = await db.Matches.IgnoreQueryFilters().AsNoTracking().LongCountAsync(ct);

            matchAggregates = (await db.Matches
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .Where(x => x.Patch == activePatchVersion)
                    .GroupBy(x => x.PlatformRegion)
                    .Select(g => new
                    {
                        Region = g.Key,
                        Total = g.LongCount(),
                        Successful = g.LongCount(x => x.Status == FetchStatus.Success),
                        TemporaryFailure = g.LongCount(x => x.Status == FetchStatus.TemporaryFailure),
                        Unfetched = g.LongCount(x => x.Status == FetchStatus.Unfetched),
                        PermanentlyUnfetchable = g.LongCount(x => x.Status == FetchStatus.PermanentlyUnfetchable),
                        OutsideRetention = g.LongCount(x => x.Status == FetchStatus.OutsideRetentionWindow),
                        RankedSuccessful =
                            g.LongCount(x => x.Status == FetchStatus.Success &&
                                             x.QueueId == QueueCatalog.RankedSoloDuoQueueId),
                        LatestSuccessfulFetchUtc =
                            g.Where(x => x.Status == FetchStatus.Success && x.FetchedAt != null).Max(x => x.FetchedAt)
                    })
                    .ToListAsync(ct))
                .ToDictionary(
                    x => NormalizeRegionKey(x.Region),
                    x => new RegionMatchAggregate(
                        NormalizeRegionKey(x.Region),
                        x.Total,
                        x.Successful,
                        x.TemporaryFailure,
                        x.Unfetched,
                        x.PermanentlyUnfetchable,
                        x.OutsideRetention,
                        x.RankedSuccessful,
                        x.LatestSuccessfulFetchUtc),
                    StringComparer.OrdinalIgnoreCase);

            timelineAggregates = (await db.MatchTimelineFetchStates
                    .AsNoTracking()
                    .Where(x => x.Match.Patch == activePatchVersion && x.Match.QueueId == QueueCatalog.RankedSoloDuoQueueId)
                    .GroupBy(x => x.Match.PlatformRegion)
                    .Select(g => new
                    {
                        Region = g.Key,
                        Successful = g.LongCount(x => x.Status == MatchTimelineFetchStatus.Success),
                        Pending = g.LongCount(x => x.Status == MatchTimelineFetchStatus.Unfetched),
                        TemporaryFailure = g.LongCount(x => x.Status == MatchTimelineFetchStatus.TemporaryFailure),
                        PermanentFailure = g.LongCount(x => x.Status == MatchTimelineFetchStatus.PermanentlyFailed),
                        NotApplicable = g.LongCount(x => x.Status == MatchTimelineFetchStatus.NotApplicable)
                    })
                    .ToListAsync(ct))
                .ToDictionary(
                    x => NormalizeRegionKey(x.Region),
                    x => new RegionTimelineAggregate(
                        NormalizeRegionKey(x.Region),
                        x.Successful,
                        x.Pending,
                        x.TemporaryFailure,
                        x.PermanentFailure,
                        x.NotApplicable),
                    StringComparer.OrdinalIgnoreCase);

            currentPatchSuccessfulMatches = matchAggregates.Values.Sum(x => x.Successful);
            currentPatchRankedMatches = matchAggregates.Values.Sum(x => x.RankedSuccessful);
            currentPatchTimelineSuccess = timelineAggregates.Values.Sum(x => x.Successful);
            currentPatchTimelineEligible = timelineAggregates.Values.Sum(x =>
                x.Successful + x.Pending + x.TemporaryFailure + x.PermanentFailure);
        }

        var refreshLockSnapshot = await db.RefreshLocks
            .AsNoTracking()
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Active = g.Count(x => x.LockedUntilUtc >= generatedAtUtc),
                Expired = g.Count(x => x.LockedUntilUtc < generatedAtUtc)
            })
            .FirstOrDefaultAsync(ct);

        var allRegions = enabledRegions
            .Concat(summonerCounts.Keys)
            .Concat(matchAggregates.Keys)
            .Concat(timelineAggregates.Keys)
            .Concat(proCounts.Keys)
            .Concat(backlogByRegion.Keys)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var regions = allRegions
            .Select(region =>
            {
                matchAggregates.TryGetValue(region, out var matchAggregate);
                timelineAggregates.TryGetValue(region, out var timelineAggregate);
                backlogByRegion.TryGetValue(region, out var backlogAggregate);
                summonerCounts.TryGetValue(region, out var regionSummoners);
                proCounts.TryGetValue(region, out var regionPros);

                var successful = matchAggregate?.Successful ?? 0;
                var latestSuccessfulFetchUtc = matchAggregate?.LatestSuccessfulFetchUtc;
                var hasBacklog = (backlogAggregate?.Enqueued ?? 0) + (backlogAggregate?.Processing ?? 0) +
                    (backlogAggregate?.Scheduled ?? 0) > 0;
                var isStale = !latestSuccessfulFetchUtc.HasValue || latestSuccessfulFetchUtc.Value < staleCutoffUtc;
                var health = successful >= Math.Max(1, ingestionOptions.Value.MinimumSuccessfulMatchesForCurrentPatch) &&
                    !isStale
                    ? "healthy"
                    : hasBacklog && (backlogAggregate?.Processing ?? 0) > 0
                        ? "catching_up"
                        : hasBacklog
                            ? "blocked"
                            : "stale";

                return new AdminAnalysisRegionMetricsDto(
                    Region: region,
                    Enabled: enabledRegions.Contains(region),
                    Summoners: regionSummoners,
                    CurrentPatchTotalMatches: matchAggregate?.Total ?? 0,
                    CurrentPatchSuccessfulMatches: successful,
                    CurrentPatchTemporaryFailures: matchAggregate?.TemporaryFailure ?? 0,
                    CurrentPatchUnfetchedMatches: matchAggregate?.Unfetched ?? 0,
                    CurrentPatchPermanentlyUnfetchableMatches: matchAggregate?.PermanentlyUnfetchable ?? 0,
                    CurrentPatchOutsideRetentionMatches: matchAggregate?.OutsideRetention ?? 0,
                    RankedCurrentPatchMatches: matchAggregate?.RankedSuccessful ?? 0,
                    LatestSuccessfulFetchUtc: latestSuccessfulFetchUtc,
                    TimelineSuccessfulMatches: timelineAggregate?.Successful ?? 0,
                    TimelinePendingMatches: timelineAggregate?.Pending ?? 0,
                    TimelineTemporaryFailures: timelineAggregate?.TemporaryFailure ?? 0,
                    TimelinePermanentFailures: timelineAggregate?.PermanentFailure ?? 0,
                    TrackedProSummoners: regionPros,
                    EnqueuedJobs: backlogAggregate?.Enqueued ?? 0,
                    ProcessingJobs: backlogAggregate?.Processing ?? 0,
                    ScheduledJobs: backlogAggregate?.Scheduled ?? 0,
                    Health: health);
            })
            .ToList();

        return Ok(new AdminAnalysisMetricsResponse(
            GeneratedAtUtc: generatedAtUtc,
            ActivePatchVersion: activePatchVersion,
            ActivePatchReleasedAtUtc: activePatch?.ReleaseDate,
            Summary: new AdminAnalysisSummaryDto(
                Summoners: summonerCounts.Values.Sum(),
                Matches: totalMatches,
                CurrentPatchSuccessfulMatches: currentPatchSuccessfulMatches,
                CurrentPatchRankedMatches: currentPatchRankedMatches,
                TimelineCoverageRatio: currentPatchTimelineEligible <= 0
                    ? 0
                    : Math.Round((double)currentPatchTimelineSuccess / currentPatchTimelineEligible, 4),
                ActiveRefreshLocks: refreshLockSnapshot?.Active ?? 0,
                ExpiredRefreshLocks: refreshLockSnapshot?.Expired ?? 0,
                TrackedProSummoners: proCounts.Values.Sum()),
            Regions: regions));
    }

    [HttpGet("jobs/recurring")]
    [ProducesResponseType(typeof(IReadOnlyList<AdminRecurringJobDto>), StatusCodes.Status200OK)]
    public IActionResult GetRecurringJobs()
    {
        using var connection = jobStorage.GetConnection();
        var storedJobs = connection.GetRecurringJobs()
            .ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);
        var descriptors = recurringJobPolicy.BuildDescriptors(workerScheduleOptions.Value);
        var jobs = new List<AdminRecurringJobDto>();

        foreach (var descriptor in descriptors.OrderBy(x => x.JobId, StringComparer.Ordinal))
        {
            storedJobs.TryGetValue(descriptor.JobId, out var stored);
            jobs.Add(new AdminRecurringJobDto(
                Id: descriptor.JobId,
                Queue: stored?.Queue ?? "default",
                Cron: stored?.Cron ?? descriptor.CronExpression,
                NextExecution: stored?.NextExecution,
                LastExecution: stored?.LastExecution,
                LastJobId: stored?.LastJobId,
                LastJobState: stored?.LastJobState,
                Error: stored?.Error,
                IsPresentInStorage: stored is not null,
                IsEnabledByConfiguration: descriptor.IsEnabled,
                IsPaused: descriptor.IsEnabled && stored is null,
                IsPausable: PausableRecurringJobIds.Contains(descriptor.JobId)));
        }

        foreach (var extra in storedJobs.Values
                     .Where(x => descriptors.All(d => !string.Equals(d.JobId, x.Id, StringComparison.OrdinalIgnoreCase)))
                     .OrderBy(x => x.Id, StringComparer.Ordinal))
        {
            jobs.Add(new AdminRecurringJobDto(
                Id: extra.Id,
                Queue: extra.Queue ?? "default",
                Cron: extra.Cron,
                NextExecution: extra.NextExecution,
                LastExecution: extra.LastExecution,
                LastJobId: extra.LastJobId,
                LastJobState: extra.LastJobState,
                Error: extra.Error,
                IsPresentInStorage: true,
                IsEnabledByConfiguration: false,
                IsPaused: false,
                IsPausable: false));
        }

        return Ok(jobs);
    }

    [HttpPost("jobs/recurring/{id}/trigger")]
    [EnableRateLimiting("admin-write")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> TriggerRecurring([FromRoute] string id, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(id))
            return BadRequest("Recurring job id is required.");

        try
        {
            RecurringJob.TriggerJob(id.Trim());
            await WriteAuditAsync("jobs.recurring.trigger", "recurring-job", id, true, null, ct);
            return Ok(new { message = "Recurring job triggered.", id });
        }
        catch (Exception ex)
        {
            await WriteAuditAsync(
                "jobs.recurring.trigger",
                "recurring-job",
                id,
                false,
                new { error = ex.Message },
                ct);
            return BadRequest(new { message = "Unable to trigger recurring job.", detail = ex.Message });
        }
    }

    [HttpPost("jobs/recurring/{id}/pause")]
    [EnableRateLimiting("admin-write")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> PauseRecurring([FromRoute] string id, CancellationToken ct)
    {
        var normalizedId = id.Trim();
        if (!PausableRecurringJobIds.Contains(normalizedId))
            return BadRequest(new { message = "Only producer recurring jobs can be paused from admin." });

        try
        {
            RecurringJob.RemoveIfExists(normalizedId);
            await WriteAuditAsync("jobs.recurring.pause", "recurring-job", normalizedId, true, null, ct);
            return Ok(new { message = "Recurring job paused.", id = normalizedId });
        }
        catch (Exception ex)
        {
            await WriteAuditAsync(
                "jobs.recurring.pause",
                "recurring-job",
                normalizedId,
                false,
                new { error = ex.Message },
                ct);
            return BadRequest(new { message = "Unable to pause recurring job.", detail = ex.Message });
        }
    }

    [HttpPost("jobs/recurring/{id}/resume")]
    [EnableRateLimiting("admin-write")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ResumeRecurring([FromRoute] string id, CancellationToken ct)
    {
        var normalizedId = id.Trim();
        var descriptor = recurringJobPolicy
            .BuildDescriptors(workerScheduleOptions.Value)
            .FirstOrDefault(x => string.Equals(x.JobId, normalizedId, StringComparison.OrdinalIgnoreCase));
        if (descriptor is null || !PausableRecurringJobIds.Contains(normalizedId))
            return BadRequest(new { message = "Only producer recurring jobs can be resumed from admin." });
        if (!descriptor.IsEnabled)
            return BadRequest(new { message = "Recurring job is disabled by configuration and cannot be resumed." });

        try
        {
            descriptor.Apply(recurringJobManager);
            await WriteAuditAsync("jobs.recurring.resume", "recurring-job", normalizedId, true, null, ct);
            return Ok(new { message = "Recurring job resumed.", id = normalizedId });
        }
        catch (Exception ex)
        {
            await WriteAuditAsync(
                "jobs.recurring.resume",
                "recurring-job",
                normalizedId,
                false,
                new { error = ex.Message },
                ct);
            return BadRequest(new { message = "Unable to resume recurring job.", detail = ex.Message });
        }
    }

    [HttpGet("jobs/queues")]
    [ProducesResponseType(typeof(AdminQueueSummaryResponse), StatusCodes.Status200OK)]
    public IActionResult GetQueueSummary([FromQuery] int scanLimit = DefaultJobScanLimit)
    {
        var monitoring = jobStorage.GetMonitoringApi();
        var safeScanLimit = Math.Clamp(scanLimit, 100, 50000);
        var jobs = ScanJobs(monitoring, ["enqueued", "processing", "scheduled", "failed"], safeScanLimit, out var truncated);
        var groups = jobs
            .GroupBy(x => new { x.State, x.Queue, x.JobType, x.JobMethod, x.Region })
            .Select(g => new AdminJobGroupDto(
                State: g.Key.State,
                Queue: g.Key.Queue,
                JobType: g.Key.JobType,
                JobMethod: g.Key.JobMethod,
                Region: g.Key.Region,
                Count: g.LongCount(),
                OldestSeenAtUtc: g.Min(x => x.StateChangedAtUtc),
                NewestSeenAtUtc: g.Max(x => x.StateChangedAtUtc)))
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.State, StringComparer.Ordinal)
            .Take(25)
            .ToList();

        return Ok(new AdminQueueSummaryResponse(
            GeneratedAtUtc: DateTime.UtcNow,
            Truncated: truncated,
            ScanLimit: safeScanLimit,
            Queues: monitoring.Queues()
                .Select(q => new AdminQueueSnapshot(q.Name, q.Length, q.Fetched))
                .OrderByDescending(x => x.Length)
                .ToList(),
            TopGroups: groups));
    }

    [HttpGet("jobs/list")]
    [ProducesResponseType(typeof(AdminJobListResponse), StatusCodes.Status200OK)]
    public IActionResult GetJobs(
        [FromQuery] string state = "failed",
        [FromQuery] string? queue = null,
        [FromQuery] string? type = null,
        [FromQuery] string? region = null,
        [FromQuery] string? q = null,
        [FromQuery] int? olderThanMinutes = null,
        [FromQuery] int from = 0,
        [FromQuery] int count = 25,
        [FromQuery] int scanLimit = DefaultJobScanLimit)
    {
        var normalizedStates = NormalizeRequestedStates([state]);
        if (normalizedStates.Count == 0)
            return BadRequest(new { message = "Unsupported state. Allowed values: enqueued, processing, scheduled, failed." });

        var safeFrom = Math.Max(0, from);
        var safeCount = Math.Clamp(count, 1, 200);
        var safeScanLimit = Math.Clamp(scanLimit, 100, 50000);
        var monitoring = jobStorage.GetMonitoringApi();
        var jobs = ScanJobs(monitoring, normalizedStates, safeScanLimit, out var truncated);
        var filtered = ApplyJobFilters(jobs, queue, type, region, q, olderThanMinutes).ToList();
        var page = filtered.Skip(safeFrom).Take(safeCount).ToList();

        return Ok(new AdminJobListResponse(
            GeneratedAtUtc: DateTime.UtcNow,
            From: safeFrom,
            Count: safeCount,
            TotalMatched: filtered.Count,
            Truncated: truncated,
            ScanLimit: safeScanLimit,
            Items: page));
    }

    [HttpGet("jobs/failed")]
    [ProducesResponseType(typeof(IReadOnlyList<AdminFailedJobDto>), StatusCodes.Status200OK)]
    public IActionResult GetFailedJobs([FromQuery] int from = 0, [FromQuery] int count = 25)
    {
        var monitoring = jobStorage.GetMonitoringApi();
        var safeFrom = Math.Max(0, from);
        var safeCount = Math.Clamp(count, 1, 100);
        var failed = monitoring.FailedJobs(safeFrom, safeCount)
            .Select(x => new AdminFailedJobDto(
                x.Key,
                x.Value.Reason,
                x.Value.ExceptionType,
                x.Value.ExceptionMessage,
                x.Value.FailedAt))
            .ToList();

        return Ok(failed);
    }

    [HttpGet("jobs/inspect/{jobId}")]
    [ProducesResponseType(typeof(AdminJobDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult GetJobDetail([FromRoute] string jobId)
    {
        if (string.IsNullOrWhiteSpace(jobId))
            return NotFound();

        var dto = BuildJobDetail(jobId.Trim());
        return dto is null
            ? NotFound(new { message = "Job not found.", jobId = jobId.Trim() })
            : Ok(dto);
    }

    [HttpGet("jobs/failed/{jobId}")]
    [ProducesResponseType(typeof(AdminJobDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult GetFailedJobDetail([FromRoute] string jobId)
    {
        if (string.IsNullOrWhiteSpace(jobId))
            return NotFound();

        var dto = BuildJobDetail(jobId.Trim());
        if (dto is null)
            return NotFound(new { message = "Job not found.", jobId = jobId.Trim() });
        if (!string.Equals(dto.CurrentState, "Failed", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = "Job is not currently in the failed state.", jobId = jobId.Trim() });

        return Ok(dto);
    }

    [HttpPost("jobs/failed/{jobId}/retry")]
    [EnableRateLimiting("admin-write")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> RetryFailedJob([FromRoute] string jobId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(jobId))
            return BadRequest("Job id is required.");

        try
        {
            BackgroundJob.Requeue(jobId.Trim());
            await WriteAuditAsync("jobs.failed.retry", "background-job", jobId, true, null, ct);
            return Ok(new { message = "Failed job re-queued.", jobId });
        }
        catch (Exception ex)
        {
            await WriteAuditAsync(
                "jobs.failed.retry",
                "background-job",
                jobId,
                false,
                new { error = ex.Message },
                ct);
            return BadRequest(new { message = "Unable to re-queue failed job.", detail = ex.Message });
        }
    }

    [HttpPost("jobs/inspect/{jobId}/delete")]
    [EnableRateLimiting("admin-write")]
    [ProducesResponseType(typeof(AdminDeleteJobResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> DeleteJob(
        [FromRoute] string jobId,
        [FromBody] AdminDeleteJobRequest? request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(jobId))
            return BadRequest("Job id is required.");

        var normalizedId = jobId.Trim();
        var expectedState = string.IsNullOrWhiteSpace(request?.ExpectedState) ? null : request.ExpectedState.Trim();

        try
        {
            var detailBeforeDelete = BuildJobDetail(normalizedId);
            var deleted = string.IsNullOrWhiteSpace(expectedState)
                ? BackgroundJob.Delete(normalizedId)
                : BackgroundJob.Delete(normalizedId, expectedState);
            var currentState = deleted
                ? "Deleted"
                : BuildJobDetail(normalizedId)?.CurrentState ?? detailBeforeDelete?.CurrentState;
            var message = BuildDeleteMessage(normalizedId, deleted, expectedState, currentState);
            await WriteAuditAsync(
                "jobs.delete",
                "background-job",
                normalizedId,
                deleted,
                new { expectedState, currentState, request?.Reason, message },
                ct);
            return Ok(new AdminDeleteJobResultDto
            {
                JobId = normalizedId,
                Deleted = deleted,
                ExpectedState = expectedState,
                CurrentState = currentState,
                Message = message
            });
        }
        catch (Exception ex)
        {
            await WriteAuditAsync(
                "jobs.delete",
                "background-job",
                normalizedId,
                false,
                new { expectedState, request?.Reason, error = ex.Message },
                ct);
            return BadRequest(new { message = "Unable to delete job.", detail = ex.Message });
        }
    }

    [HttpPost("jobs/bulk-delete")]
    [EnableRateLimiting("admin-write")]
    [ProducesResponseType(typeof(AdminBulkDeleteJobsResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> BulkDeleteJobs([FromBody] AdminBulkDeleteJobsRequest request, CancellationToken ct)
    {
        var states = NormalizeRequestedStates(request.States?.Count > 0 ? request.States : ["enqueued", "scheduled", "failed"]);
        if (states.Count == 0 || states.Any(x => !BulkDeletableStates.Contains(x)))
        {
            return BadRequest(new
            {
                message = "Bulk delete only supports enqueued, scheduled, and failed states."
            });
        }

        var safeLimit = Math.Clamp(request.Limit ?? 500, 1, 5000);
        var safeScanLimit = Math.Clamp(request.ScanLimit ?? DefaultJobScanLimit, 100, 50000);
        var monitoring = jobStorage.GetMonitoringApi();
        var candidates = ApplyJobFilters(
                ScanJobs(monitoring, states, safeScanLimit, out var truncated),
                queue: null,
                request.JobType,
                request.Region,
                request.Query,
                request.OlderThanMinutes,
                request.Queues)
            .Take(safeLimit)
            .ToList();

        var deleted = 0;
        var failed = 0;
        if (!request.DryRun)
        {
            foreach (var candidate in candidates)
            {
                if (BackgroundJob.Delete(candidate.JobId, candidate.State))
                    deleted++;
                else
                    failed++;
            }
        }

        await WriteAuditAsync(
            "jobs.bulk-delete",
            "background-job",
            null,
            isSuccess: !request.DryRun ? failed == 0 : true,
            metadata: new
            {
                states,
                request.Queues,
                request.JobType,
                request.Region,
                request.Query,
                request.OlderThanMinutes,
                limit = safeLimit,
                scanLimit = safeScanLimit,
                request.DryRun,
                matched = candidates.Count,
                deleted,
                failed,
                truncated
            },
            ct: ct);

        return Ok(new AdminBulkDeleteJobsResultDto(
            DryRun: request.DryRun,
            Truncated: truncated,
            Matched: candidates.Count,
            Deleted: deleted,
            Failed: failed,
            SampleJobIds: candidates.Take(20).Select(x => x.JobId).ToList()));
    }

    [HttpPost("cache/invalidate")]
    [EnableRateLimiting("admin-write")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> InvalidateAnalyticsCache(CancellationToken ct)
    {
        await analyticsService.InvalidateAnalyticsCacheAsync(ct);
        await WriteAuditAsync("cache.invalidate", "analytics-cache", null, true, null, ct);
        return Ok(new { message = "Analytics cache invalidated." });
    }

    [HttpGet("audit-log")]
    [ProducesResponseType(typeof(IReadOnlyList<AdminAuditEntryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAuditLog([FromQuery] int limit = 100, CancellationToken ct = default)
    {
        var rows = await adminAuditService.ListRecentAsync(limit, ct);
        return Ok(rows);
    }

    [HttpGet("logs/services")]
    [ProducesResponseType(typeof(AdminServiceLogsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public IActionResult GetServiceLogs(
        [FromQuery] string service = "service",
        [FromQuery] string? level = null,
        [FromQuery] string? q = null,
        [FromQuery] DateTime? sinceUtc = null,
        [FromQuery] DateTime? untilUtc = null,
        [FromQuery] int limit = 200)
    {
        var serviceKey = service.Trim().ToLowerInvariant();
        if (!AllowedServiceLogKeys.Contains(serviceKey))
        {
            return BadRequest(new
            {
                message = "Unsupported service. Allowed values: webapi, service."
            });
        }

        var safeLimit = Math.Clamp(limit, 1, 500);
        var directory = ResolveOperationalLogDirectory(serviceKey);
        var logFiles = directory is null
            ? []
            : GetOperationalLogFiles(directory, serviceKey).ToList();
        if (logFiles.Count == 0)
        {
            return Ok(new AdminServiceLogsResponse(
                new AdminLogSourceDto(
                    Service: serviceKey,
                    Available: false,
                    FilesScanned: 0,
                    LatestTimestampUtc: null,
                    Truncated: false),
                Array.Empty<AdminServiceLogDto>()));
        }

        var normalizedLevel = string.IsNullOrWhiteSpace(level)
            ? null
            : level.Trim().ToUpperInvariant();
        var search = string.IsNullOrWhiteSpace(q) ? null : q.Trim();

        var entries = new List<AdminServiceLogDto>(safeLimit);
        DateTime? latestTimestampUtc = null;
        var truncated = false;

        foreach (var path in logFiles)
        {
            foreach (var line in ReadMostRecentLines(path, maxLines: 4000))
            {
                if (!TryParseOperationalLogLine(line, out var parsed))
                    continue;

                latestTimestampUtc ??= parsed.TimestampUtc;

                if (normalizedLevel is not null && !string.Equals(parsed.Level, normalizedLevel, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (sinceUtc.HasValue && parsed.TimestampUtc < sinceUtc.Value)
                    continue;

                if (untilUtc.HasValue && parsed.TimestampUtc > untilUtc.Value)
                    continue;

                if (search is not null &&
                    !ContainsSearch(parsed.Message, search) &&
                    !ContainsSearch(parsed.Category, search) &&
                    !ContainsSearch(parsed.Exception, search))
                    continue;

                entries.Add(parsed);
                if (entries.Count >= safeLimit)
                {
                    truncated = true;
                    break;
                }
            }

            if (entries.Count >= safeLimit)
                break;
        }

        return Ok(new AdminServiceLogsResponse(
            new AdminLogSourceDto(
                Service: serviceKey,
                Available: true,
                FilesScanned: logFiles.Count,
                LatestTimestampUtc: latestTimestampUtc,
                Truncated: truncated),
            entries));
    }

    private AdminJobDetailDto? BuildJobDetail(string safeJobId)
    {
        var monitoring = jobStorage.GetMonitoringApi();
        var details = monitoring.JobDetails(safeJobId);
        if (details is null)
            return null;

        var history = details.History ?? [];
        var states = history
            .Select(MapStateTransition)
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToList();
        var currentState = states.FirstOrDefault();
        var failedState = states.FirstOrDefault(x => string.Equals(x.StateName, "Failed", StringComparison.OrdinalIgnoreCase));
        var processingState = states.FirstOrDefault(x => string.Equals(x.StateName, "Processing", StringComparison.OrdinalIgnoreCase));
        var enqueuedState = states.FirstOrDefault(x => string.Equals(x.StateName, "Enqueued", StringComparison.OrdinalIgnoreCase));
        var scheduledState = states.FirstOrDefault(x => string.Equals(x.StateName, "Scheduled", StringComparison.OrdinalIgnoreCase));
        var deletedState = states.FirstOrDefault(x => string.Equals(x.StateName, "Deleted", StringComparison.OrdinalIgnoreCase));

        var queue = GetDictionaryValue(currentState?.Data, "Queue") ??
            GetDictionaryValue(enqueuedState?.Data, "Queue") ??
            GetDictionaryValue(scheduledState?.Data, "Queue") ??
            "default";

        return new AdminJobDetailDto(
            JobId: safeJobId,
            CurrentState: currentState?.StateName,
            Queue: queue,
            JobType: details.Job?.Type?.FullName,
            JobMethod: details.Job?.Method?.Name,
            Arguments: details.Job?.Args?.Select(SafeSerialize).ToList() ?? [],
            Region: InferRegion(details.Job),
            CreatedAtUtc: details.CreatedAt,
            EnqueuedAtUtc: enqueuedState?.CreatedAtUtc,
            ScheduledAtUtc: scheduledState?.CreatedAtUtc,
            StartedAtUtc: processingState?.CreatedAtUtc,
            StateChangedAtUtc: currentState?.CreatedAtUtc,
            FailedAtUtc: failedState?.CreatedAtUtc,
            DeletedAtUtc: deletedState?.CreatedAtUtc,
            ServerId: GetDictionaryValue(processingState?.Data, "ServerId") ??
                GetDictionaryValue(currentState?.Data, "ServerId"),
            Reason: failedState?.Reason ?? currentState?.Reason,
            ExceptionType: GetDictionaryValue(failedState?.Data, "ExceptionType"),
            ExceptionMessage: GetDictionaryValue(failedState?.Data, "ExceptionMessage"),
            ExceptionDetails: GetDictionaryValue(failedState?.Data, "ExceptionDetails"),
            FailedCount: states.Count(x => string.Equals(x.StateName, "Failed", StringComparison.OrdinalIgnoreCase)),
            States: states,
            Properties: details.Properties is null
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : new Dictionary<string, string>(details.Properties, StringComparer.Ordinal));
    }

    private static string BuildDeleteMessage(
        string jobId,
        bool deleted,
        string? expectedState,
        string? currentState)
    {
        if (deleted)
            return $"Job {jobId} deleted.";

        if (!string.IsNullOrWhiteSpace(expectedState) && !string.IsNullOrWhiteSpace(currentState))
        {
            return
                $"Job {jobId} was not deleted because it is now in state {currentState} instead of {expectedState}.";
        }

        if (!string.IsNullOrWhiteSpace(currentState))
            return $"Job {jobId} was not deleted. Current state: {currentState}.";

        return $"Job {jobId} was not deleted because it no longer exists or its state could not be resolved.";
    }

    private IReadOnlyList<string> GetEnabledRegions()
    {
        var multiRegion = multiRegionOptions.Value;
        if (multiRegion.Enabled && multiRegion.Regions.Count > 0)
        {
            return multiRegion.Regions
                .Where(x => x.Enabled && !string.IsNullOrWhiteSpace(x.Region))
                .Select(x => NormalizeRegionKey(x.Region))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        return KnownPlatformRegions.ToList();
    }

    private IReadOnlyDictionary<string, AdminBacklogRegionAggregate> BuildBacklogByRegion(
        IReadOnlyList<AdminJobListItemDto> jobs)
    {
        return jobs
            .Where(x => !string.IsNullOrWhiteSpace(x.Region))
            .GroupBy(x => NormalizeRegionKey(x.Region))
            .ToDictionary(
                g => g.Key,
                g => new AdminBacklogRegionAggregate(
                    Enqueued: g.LongCount(x => string.Equals(x.State, "Enqueued", StringComparison.OrdinalIgnoreCase)),
                    Processing: g.LongCount(x => string.Equals(x.State, "Processing", StringComparison.OrdinalIgnoreCase)),
                    Scheduled: g.LongCount(x => string.Equals(x.State, "Scheduled", StringComparison.OrdinalIgnoreCase))),
                StringComparer.OrdinalIgnoreCase);
    }

    private IReadOnlyList<AdminJobListItemDto> ScanJobs(
        IMonitoringApi monitoring,
        IReadOnlyCollection<string> requestedStates,
        int scanLimit,
        out bool truncated)
    {
        var items = new List<AdminJobListItemDto>(Math.Min(scanLimit, 2000));
        var truncatedLocal = false;

        bool Add(AdminJobListItemDto item)
        {
            items.Add(item);
            if (items.Count < scanLimit)
                return true;

            truncatedLocal = true;
            return false;
        }

        if (requestedStates.Contains("enqueued"))
        {
            foreach (var queue in monitoring.Queues())
            {
                for (var offset = 0; offset < queue.Length; offset += DefaultJobScanPageSize)
                {
                    foreach (var row in monitoring.EnqueuedJobs(queue.Name, offset, DefaultJobScanPageSize))
                    {
                        if (!Add(MapEnqueuedJob(queue.Name, row)))
                        {
                            truncated = truncatedLocal;
                            return items;
                        }
                    }
                }
            }
        }

        if (requestedStates.Contains("processing"))
        {
            var total = monitoring.ProcessingCount();
            for (var offset = 0; offset < total; offset += DefaultJobScanPageSize)
            {
                foreach (var row in monitoring.ProcessingJobs(offset, DefaultJobScanPageSize))
                {
                        if (!Add(MapProcessingJob(row)))
                        {
                            truncated = truncatedLocal;
                            return items;
                        }
                }
            }
        }

        if (requestedStates.Contains("scheduled"))
        {
            var total = monitoring.ScheduledCount();
            for (var offset = 0; offset < total; offset += DefaultJobScanPageSize)
            {
                foreach (var row in monitoring.ScheduledJobs(offset, DefaultJobScanPageSize))
                {
                        if (!Add(MapScheduledJob(row)))
                        {
                            truncated = truncatedLocal;
                            return items;
                        }
                }
            }
        }

        if (requestedStates.Contains("failed"))
        {
            var total = monitoring.FailedCount();
            for (var offset = 0; offset < total; offset += DefaultJobScanPageSize)
            {
                foreach (var row in monitoring.FailedJobs(offset, DefaultJobScanPageSize))
                {
                        if (!Add(MapFailedJob(row)))
                        {
                            truncated = truncatedLocal;
                            return items;
                        }
                }
            }
        }

        truncated = truncatedLocal;
        return items;
    }

    private IEnumerable<AdminJobListItemDto> ApplyJobFilters(
        IReadOnlyList<AdminJobListItemDto> jobs,
        string? queue,
        string? jobType,
        string? region,
        string? query,
        int? olderThanMinutes,
        IReadOnlyCollection<string>? queues = null)
    {
        var normalizedQueue = string.IsNullOrWhiteSpace(queue) ? null : queue.Trim();
        var normalizedType = string.IsNullOrWhiteSpace(jobType) ? null : jobType.Trim();
        var normalizedRegion = string.IsNullOrWhiteSpace(region) ? null : NormalizeRegionKey(region);
        var normalizedQuery = string.IsNullOrWhiteSpace(query) ? null : query.Trim();
        var queueSet = queues?
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var olderThanCutoffUtc = olderThanMinutes.HasValue && olderThanMinutes.Value > 0
            ? DateTime.UtcNow.AddMinutes(-olderThanMinutes.Value)
            : (DateTime?)null;

        return jobs.Where(job =>
        {
            if (normalizedQueue is not null && !string.Equals(job.Queue, normalizedQueue, StringComparison.OrdinalIgnoreCase))
                return false;
            if (queueSet is not null && queueSet.Count > 0 && !queueSet.Contains(job.Queue))
                return false;
            if (normalizedType is not null &&
                !(job.JobType?.Contains(normalizedType, StringComparison.OrdinalIgnoreCase) ?? false))
                return false;
            if (normalizedRegion is not null &&
                !string.Equals(job.Region, normalizedRegion, StringComparison.OrdinalIgnoreCase))
                return false;
            if (olderThanCutoffUtc.HasValue &&
                job.StateChangedAtUtc.HasValue &&
                job.StateChangedAtUtc.Value > olderThanCutoffUtc.Value)
                return false;
            if (normalizedQuery is null)
                return true;

            var haystack = string.Join(
                " ",
                [
                    job.JobId,
                    job.State,
                    job.Queue,
                    job.JobType ?? string.Empty,
                    job.JobMethod ?? string.Empty,
                    job.Region ?? string.Empty,
                    job.Reason ?? string.Empty,
                    job.ExceptionType ?? string.Empty,
                    job.ExceptionMessage ?? string.Empty,
                    string.Join(" ", job.Arguments)
                ]);
            return haystack.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase);
        });
    }

    private AdminJobListItemDto MapEnqueuedJob(string queueName, KeyValuePair<string, EnqueuedJobDto> row)
    {
        var dto = row.Value;
        return new AdminJobListItemDto(
            JobId: row.Key,
            State: "Enqueued",
            Queue: queueName,
            JobType: dto.Job?.Type?.FullName,
            JobMethod: dto.Job?.Method?.Name,
            Region: InferRegion(dto.Job),
            CreatedAtUtc: null,
            StateChangedAtUtc: dto.EnqueuedAt,
            StartedAtUtc: null,
            ServerId: null,
            Reason: dto.State,
            ExceptionType: null,
            ExceptionMessage: null,
            Arguments: dto.Job?.Args?.Select(SafeSerialize).ToList() ?? []);
    }

    private AdminJobListItemDto MapProcessingJob(KeyValuePair<string, ProcessingJobDto> row)
    {
        var dto = row.Value;
        return new AdminJobListItemDto(
            JobId: row.Key,
            State: "Processing",
            Queue: GetDictionaryValue(dto.StateData, "Queue") ?? "default",
            JobType: dto.Job?.Type?.FullName,
            JobMethod: dto.Job?.Method?.Name,
            Region: InferRegion(dto.Job),
            CreatedAtUtc: null,
            StateChangedAtUtc: dto.StartedAt,
            StartedAtUtc: dto.StartedAt,
            ServerId: dto.ServerId,
            Reason: null,
            ExceptionType: null,
            ExceptionMessage: null,
            Arguments: dto.Job?.Args?.Select(SafeSerialize).ToList() ?? []);
    }

    private AdminJobListItemDto MapScheduledJob(KeyValuePair<string, ScheduledJobDto> row)
    {
        var dto = row.Value;
        return new AdminJobListItemDto(
            JobId: row.Key,
            State: "Scheduled",
            Queue: GetDictionaryValue(dto.StateData, "Queue") ?? "default",
            JobType: dto.Job?.Type?.FullName,
            JobMethod: dto.Job?.Method?.Name,
            Region: InferRegion(dto.Job),
            CreatedAtUtc: null,
            StateChangedAtUtc: dto.ScheduledAt ?? dto.EnqueueAt,
            StartedAtUtc: null,
            ServerId: null,
            Reason: $"Enqueue at {dto.EnqueueAt:u}",
            ExceptionType: null,
            ExceptionMessage: null,
            Arguments: dto.Job?.Args?.Select(SafeSerialize).ToList() ?? []);
    }

    private AdminJobListItemDto MapFailedJob(KeyValuePair<string, FailedJobDto> row)
    {
        var dto = row.Value;
        return new AdminJobListItemDto(
            JobId: row.Key,
            State: "Failed",
            Queue: GetDictionaryValue(dto.StateData, "Queue") ?? "default",
            JobType: dto.Job?.Type?.FullName,
            JobMethod: dto.Job?.Method?.Name,
            Region: InferRegion(dto.Job),
            CreatedAtUtc: null,
            StateChangedAtUtc: dto.FailedAt,
            StartedAtUtc: null,
            ServerId: GetDictionaryValue(dto.StateData, "ServerId"),
            Reason: dto.Reason,
            ExceptionType: dto.ExceptionType,
            ExceptionMessage: dto.ExceptionMessage,
            Arguments: dto.Job?.Args?.Select(SafeSerialize).ToList() ?? []);
    }

    private string? InferRegion(Job? job)
    {
        if (job?.Args is null || job.Args.Count == 0)
            return null;

        var enabledRegions = GetEnabledRegions()
            .Concat(KnownPlatformRegions)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var arg in job.Args)
        {
            if (arg is null)
                continue;

            if (arg is string raw)
            {
                var normalized = NormalizeRegionKey(raw);
                if (enabledRegions.Contains(normalized))
                    return normalized;

                foreach (var token in raw.Split([':', '/', '|', ',', ' '], StringSplitOptions.RemoveEmptyEntries))
                {
                    normalized = NormalizeRegionKey(token);
                    if (enabledRegions.Contains(normalized))
                        return normalized;
                }

                continue;
            }

            var asString = arg.ToString();
            if (string.IsNullOrWhiteSpace(asString))
                continue;

            var parsed = NormalizeRegionKey(asString);
            if (enabledRegions.Contains(parsed))
                return parsed;
        }

        return null;
    }

    private static IReadOnlyCollection<string> NormalizeRequestedStates(IReadOnlyCollection<string> states)
    {
        return states
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim().ToLowerInvariant())
            .Where(SupportedListStates.Contains)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string NormalizeRegionKey(string? region)
    {
        return string.IsNullOrWhiteSpace(region) ? "UNKNOWN" : region.Trim().ToUpperInvariant();
    }

    private static string? GetDictionaryValue(IDictionary<string, string>? data, string key)
    {
        return data is not null && data.TryGetValue(key, out var value) ? value : null;
    }

    private static string? GetDictionaryValue(IReadOnlyDictionary<string, string>? data, string key)
    {
        return data is not null && data.TryGetValue(key, out var value) ? value : null;
    }

    private async Task WriteAuditAsync(
        string action,
        string? targetType,
        string? targetId,
        bool isSuccess,
        object? metadata,
        CancellationToken ct)
    {
        var actorId = TryGetGuidClaim(ClaimTypes.NameIdentifier);
        var actorEmail = User.FindFirstValue(ClaimTypes.Name);
        var requestId = Request.Headers["x-trn-request-id"].ToString();
        await adminAuditService.WriteAsync(new AdminAuditWriteRequest(
            ActorUserAccountId: actorId,
            ActorEmail: actorEmail,
            Action: action,
            TargetType: targetType,
            TargetId: targetId,
            RequestId: string.IsNullOrWhiteSpace(requestId) ? null : requestId,
            IsSuccess: isSuccess,
            Metadata: metadata
        ), ct);
    }

    private Guid? TryGetGuidClaim(string claimType)
    {
        var value = User.FindFirstValue(claimType);
        return Guid.TryParse(value, out var parsed) ? parsed : null;
    }

    private static string SafeSerialize(object? arg)
    {
        if (arg is null)
            return "null";

        try
        {
            return JsonSerializer.Serialize(arg);
        }
        catch
        {
            return arg.ToString() ?? "<unserializable>";
        }
    }

    private static AdminJobStateTransitionDto MapStateTransition(StateHistoryDto state)
    {
        var data = state.Data ?? new Dictionary<string, string>(StringComparer.Ordinal);
        return new AdminJobStateTransitionDto(
            state.StateName,
            state.CreatedAt,
            state.Reason,
            new Dictionary<string, string>(data, StringComparer.Ordinal));
    }

    private static IEnumerable<string> ReadMostRecentLines(string path, int maxLines)
    {
        if (maxLines <= 0)
            return [];

        var lines = new List<string>(maxLines);
        var reversedLineBuffer = new List<byte>(1024);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        if (stream.Length == 0)
            return lines;

        const int readBufferSize = 4096;
        var readBuffer = new byte[readBufferSize];
        var position = stream.Length;

        while (position > 0 && lines.Count < maxLines)
        {
            var bytesToRead = (int)Math.Min(readBufferSize, position);
            position -= bytesToRead;
            stream.Seek(position, SeekOrigin.Begin);
            var bytesRead = stream.Read(readBuffer, 0, bytesToRead);

            for (var i = bytesRead - 1; i >= 0 && lines.Count < maxLines; i--)
            {
                var current = readBuffer[i];
                if (current == (byte)'\n')
                {
                    AddLineFromReversedBytes(reversedLineBuffer, lines);
                    continue;
                }

                reversedLineBuffer.Add(current);
            }
        }

        if (lines.Count < maxLines)
            AddLineFromReversedBytes(reversedLineBuffer, lines);

        return lines;
    }

    private static void AddLineFromReversedBytes(List<byte> reversedBytes, List<string> lines)
    {
        if (reversedBytes.Count == 0)
            return;

        var lineBytes = reversedBytes.ToArray();
        Array.Reverse(lineBytes);
        reversedBytes.Clear();

        var line = Encoding.UTF8.GetString(lineBytes).TrimEnd('\r');
        if (line.Length > 0)
            lines.Add(line);
    }

    private static bool TryParseOperationalLogLine(string line, out AdminServiceLogDto dto)
    {
        dto = default!;
        try
        {
            var entry = JsonSerializer.Deserialize<OperationalLogEntry>(line);
            if (entry is null)
                return false;

            dto = new AdminServiceLogDto(
                entry.TimestampUtc,
                entry.Service,
                entry.Level,
                entry.Category,
                entry.EventId,
                entry.Message,
                entry.Exception);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool ContainsSearch(string? value, string search)
    {
        return !string.IsNullOrWhiteSpace(value) &&
               value.Contains(search, StringComparison.OrdinalIgnoreCase);
    }

    private string? ResolveOperationalLogDirectory(string serviceKey)
    {
        var sourceSpecificDirectory = configuration[$"AdminLogs:Sources:{serviceKey}:DirectoryPath"];
        if (!string.IsNullOrWhiteSpace(sourceSpecificDirectory))
            return sourceSpecificDirectory.Trim();

        var sharedDirectory = configuration["OperationalLogs:DirectoryPath"];
        if (!string.IsNullOrWhiteSpace(sharedDirectory))
            return sharedDirectory.Trim();

        return string.Equals(serviceKey, "webapi", StringComparison.OrdinalIgnoreCase)
            ? Path.Combine(AppContext.BaseDirectory, "logs")
            : null;
    }

    private static IEnumerable<string> GetOperationalLogFiles(string directory, string serviceKey)
    {
        if (!Directory.Exists(directory))
            yield break;

        var candidates = Directory.EnumerateFiles(directory, $"{serviceKey}.log*")
            .Select(path => new
            {
                Path = path,
                SortOrder = GetLogFileSortOrder(path, serviceKey)
            })
            .Where(x => x.SortOrder.HasValue)
            .OrderBy(x => x.SortOrder!.Value)
            .Select(x => x.Path);

        foreach (var candidate in candidates)
            yield return candidate;
    }

    private static int? GetLogFileSortOrder(string path, string serviceKey)
    {
        var fileName = Path.GetFileName(path);
        var liveName = $"{serviceKey}.log";
        if (string.Equals(fileName, liveName, StringComparison.OrdinalIgnoreCase))
            return 0;

        var prefix = $"{liveName}.";
        if (!fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return null;

        return int.TryParse(fileName[prefix.Length..], out var parsedSuffix)
            ? parsedSuffix
            : null;
    }

    private sealed record RegionMatchAggregate(
        string Region,
        long Total,
        long Successful,
        long TemporaryFailure,
        long Unfetched,
        long PermanentlyUnfetchable,
        long OutsideRetention,
        long RankedSuccessful,
        DateTime? LatestSuccessfulFetchUtc);

    private sealed record RegionTimelineAggregate(
        string Region,
        long Successful,
        long Pending,
        long TemporaryFailure,
        long PermanentFailure,
        long NotApplicable);
}

public record AdminOverviewResponse(
    DateTime GeneratedAtUtc,
    bool DatabaseConnected,
    long Enqueued,
    long Processing,
    long Scheduled,
    long Failed,
    long Succeeded,
    long Recurring,
    long Deleted,
    int EffectiveConcurrency,
    IReadOnlyList<AdminServerSnapshot> Servers,
    IReadOnlyList<AdminQueueSnapshot> Queues
);

public record AdminServerSnapshot(
    string Name,
    int WorkersCount,
    DateTime StartedAtUtc,
    DateTime? HeartbeatUtc,
    IReadOnlyList<string> Queues
);

public record AdminQueueSnapshot(string Name, long Length, long? Fetched);

public record AdminAnalysisMetricsResponse(
    DateTime GeneratedAtUtc,
    string? ActivePatchVersion,
    DateTime? ActivePatchReleasedAtUtc,
    AdminAnalysisSummaryDto Summary,
    IReadOnlyList<AdminAnalysisRegionMetricsDto> Regions
);

public record AdminAnalysisSummaryDto(
    long Summoners,
    long Matches,
    long CurrentPatchSuccessfulMatches,
    long CurrentPatchRankedMatches,
    double TimelineCoverageRatio,
    int ActiveRefreshLocks,
    int ExpiredRefreshLocks,
    long TrackedProSummoners
);

public record AdminAnalysisRegionMetricsDto(
    string Region,
    bool Enabled,
    long Summoners,
    long CurrentPatchTotalMatches,
    long CurrentPatchSuccessfulMatches,
    long CurrentPatchTemporaryFailures,
    long CurrentPatchUnfetchedMatches,
    long CurrentPatchPermanentlyUnfetchableMatches,
    long CurrentPatchOutsideRetentionMatches,
    long RankedCurrentPatchMatches,
    DateTime? LatestSuccessfulFetchUtc,
    long TimelineSuccessfulMatches,
    long TimelinePendingMatches,
    long TimelineTemporaryFailures,
    long TimelinePermanentFailures,
    long TrackedProSummoners,
    long EnqueuedJobs,
    long ProcessingJobs,
    long ScheduledJobs,
    string Health
);

public record AdminRecurringJobDto(
    string Id,
    string Queue,
    string Cron,
    DateTime? NextExecution,
    DateTime? LastExecution,
    string? LastJobId,
    string? LastJobState,
    string? Error,
    bool IsPresentInStorage,
    bool IsEnabledByConfiguration,
    bool IsPaused,
    bool IsPausable
);

public record AdminJobGroupDto(
    string State,
    string Queue,
    string? JobType,
    string? JobMethod,
    string? Region,
    long Count,
    DateTime? OldestSeenAtUtc,
    DateTime? NewestSeenAtUtc
);

public record AdminQueueSummaryResponse(
    DateTime GeneratedAtUtc,
    bool Truncated,
    int ScanLimit,
    IReadOnlyList<AdminQueueSnapshot> Queues,
    IReadOnlyList<AdminJobGroupDto> TopGroups
);

public record AdminJobListItemDto(
    string JobId,
    string State,
    string Queue,
    string? JobType,
    string? JobMethod,
    string? Region,
    DateTime? CreatedAtUtc,
    DateTime? StateChangedAtUtc,
    DateTime? StartedAtUtc,
    string? ServerId,
    string? Reason,
    string? ExceptionType,
    string? ExceptionMessage,
    IReadOnlyList<string> Arguments
);

public record AdminJobListResponse(
    DateTime GeneratedAtUtc,
    int From,
    int Count,
    int TotalMatched,
    bool Truncated,
    int ScanLimit,
    IReadOnlyList<AdminJobListItemDto> Items
);

public record AdminFailedJobDto(
    string JobId,
    string? Reason,
    string? ExceptionType,
    string? ExceptionMessage,
    DateTime? FailedAt
);

public record AdminJobDetailDto(
    string JobId,
    string? CurrentState,
    string Queue,
    string? JobType,
    string? JobMethod,
    IReadOnlyList<string> Arguments,
    string? Region,
    DateTime? CreatedAtUtc,
    DateTime? EnqueuedAtUtc,
    DateTime? ScheduledAtUtc,
    DateTime? StartedAtUtc,
    DateTime? StateChangedAtUtc,
    DateTime? FailedAtUtc,
    DateTime? DeletedAtUtc,
    string? ServerId,
    string? Reason,
    string? ExceptionType,
    string? ExceptionMessage,
    string? ExceptionDetails,
    int FailedCount,
    IReadOnlyList<AdminJobStateTransitionDto> States,
    IReadOnlyDictionary<string, string> Properties
);

public record AdminJobStateTransitionDto(
    string StateName,
    DateTime CreatedAtUtc,
    string? Reason,
    IReadOnlyDictionary<string, string> Data
);

public record AdminDeleteJobRequest(string? ExpectedState, string? Reason);

public sealed class AdminDeleteJobResultDto
{
    [Required]
    public string JobId { get; init; } = string.Empty;

    public bool Deleted { get; init; }

    public string? ExpectedState { get; init; }

    public string? CurrentState { get; init; }

    [Required]
    public string Message { get; init; } = string.Empty;
}

public record AdminBulkDeleteJobsRequest(
    IReadOnlyList<string>? States,
    IReadOnlyList<string>? Queues,
    string? JobType,
    string? Region,
    string? Query,
    int? OlderThanMinutes,
    int? Limit,
    int? ScanLimit,
    bool DryRun
);

public record AdminBulkDeleteJobsResultDto(
    bool DryRun,
    bool Truncated,
    int Matched,
    int Deleted,
    int Failed,
    IReadOnlyList<string> SampleJobIds
);

public record AdminServiceLogDto(
    DateTime TimestampUtc,
    string Service,
    string Level,
    string Category,
    int EventId,
    string? Message,
    string? Exception
);

public record AdminLogSourceDto(
    string Service,
    bool Available,
    int FilesScanned,
    DateTime? LatestTimestampUtc,
    bool Truncated
);

public record AdminServiceLogsResponse(
    AdminLogSourceDto Source,
    IReadOnlyList<AdminServiceLogDto> Items
);

public record AdminBacklogRegionAggregate(
    long Enqueued,
    long Processing,
    long Scheduled
);
