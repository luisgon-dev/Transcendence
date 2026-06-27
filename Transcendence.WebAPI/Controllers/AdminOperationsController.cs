using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Transcendence.Service.Core.Services.Admin.Implementations;
using Transcendence.Service.Core.Services.Admin.Interfaces;
using Transcendence.Service.Core.Services.Analytics.Interfaces;
using Transcendence.Service.Core.Services.Auth.Interfaces;
using Transcendence.Service.Core.Services.Auth.Models;
using Transcendence.WebAPI.Security;

namespace Transcendence.WebAPI.Controllers;

[ApiController]
[Route("api/admin")]
[Authorize(Policy = AuthPolicies.AdminOnly)]
public class AdminOperationsController(
    IAdminJobsFacade jobsFacade,
    IAdminOverviewFacade overviewFacade,
    IAdminLogsFacade logsFacade,
    IChampionAnalyticsService analyticsService,
    IAdminAuditService adminAuditService) : ControllerBase
{
    [HttpGet("overview")]
    [ProducesResponseType(typeof(AdminOverviewResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetOverview(CancellationToken ct)
    {
        return Ok(await overviewFacade.GetOverviewAsync(ct));
    }

    [HttpGet("metrics/analysis")]
    [ProducesResponseType(typeof(AdminAnalysisMetricsResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAnalysisMetrics(CancellationToken ct)
    {
        return Ok(await overviewFacade.GetAnalysisMetricsAsync(ct));
    }

    [HttpGet("jobs/recurring")]
    [ProducesResponseType(typeof(IReadOnlyList<AdminRecurringJobDto>), StatusCodes.Status200OK)]
    public IActionResult GetRecurringJobs()
    {
        return Ok(jobsFacade.GetRecurringJobs());
    }

    [HttpPost("jobs/recurring/{id}/trigger")]
    [EnableRateLimiting("admin-write")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> TriggerRecurring([FromRoute] string id, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(id))
            return BadRequest("Recurring job id is required.");

        var result = jobsFacade.TriggerRecurring(id);
        if (result.IsSuccess)
        {
            await WriteAuditAsync("jobs.recurring.trigger", "recurring-job", id, true, null, ct);
            return Ok(new { message = "Recurring job triggered.", id });
        }

        await WriteAuditAsync(
            "jobs.recurring.trigger",
            "recurring-job",
            id,
            false,
            new { error = result.Error },
            ct);
        return BadRequest(new { message = "Unable to trigger recurring job.", detail = result.Error });
    }

    [HttpPost("jobs/recurring/{id}/pause")]
    [EnableRateLimiting("admin-write")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> PauseRecurring([FromRoute] string id, CancellationToken ct)
    {
        var normalizedId = id.Trim();
        if (!AdminJobsFacade.IsPausableRecurringJob(normalizedId))
            return BadRequest(new { message = "Only producer recurring jobs can be paused from admin." });

        var result = jobsFacade.PauseRecurring(id);
        if (result.IsSuccess)
        {
            await WriteAuditAsync("jobs.recurring.pause", "recurring-job", normalizedId, true, null, ct);
            return Ok(new { message = "Recurring job paused.", id = normalizedId });
        }

        await WriteAuditAsync(
            "jobs.recurring.pause",
            "recurring-job",
            normalizedId,
            false,
            new { error = result.Error },
            ct);
        return BadRequest(new { message = "Unable to pause recurring job.", detail = result.Error });
    }

    [HttpPost("jobs/recurring/{id}/resume")]
    [EnableRateLimiting("admin-write")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ResumeRecurring([FromRoute] string id, CancellationToken ct)
    {
        var normalizedId = id.Trim();
        var validation = jobsFacade.ValidateResume(normalizedId);
        if (validation.Kind == AdminResumeValidationKind.NotPausable)
            return BadRequest(new { message = "Only producer recurring jobs can be resumed from admin." });
        if (validation.Kind == AdminResumeValidationKind.DisabledByConfiguration)
            return BadRequest(new { message = "Recurring job is disabled by configuration and cannot be resumed." });

        var result = jobsFacade.ResumeRecurring(id);
        if (result.IsSuccess)
        {
            await WriteAuditAsync("jobs.recurring.resume", "recurring-job", normalizedId, true, null, ct);
            return Ok(new { message = "Recurring job resumed.", id = normalizedId });
        }

        await WriteAuditAsync(
            "jobs.recurring.resume",
            "recurring-job",
            normalizedId,
            false,
            new { error = result.Error },
            ct);
        return BadRequest(new { message = "Unable to resume recurring job.", detail = result.Error });
    }

    [HttpGet("jobs/queues")]
    [ProducesResponseType(typeof(AdminQueueSummaryResponse), StatusCodes.Status200OK)]
    public IActionResult GetQueueSummary([FromQuery] int scanLimit = 5000)
    {
        return Ok(jobsFacade.GetQueueSummary(scanLimit));
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
        [FromQuery] int scanLimit = 5000)
    {
        var lookup = jobsFacade.GetJobs(state, queue, type, region, q, olderThanMinutes, from, count, scanLimit);
        if (!lookup.StatesValid)
            return BadRequest(new { message = "Unsupported state. Allowed values: enqueued, processing, scheduled, failed." });

        return Ok(lookup.Response);
    }

    [HttpGet("jobs/failed")]
    [ProducesResponseType(typeof(IReadOnlyList<AdminFailedJobDto>), StatusCodes.Status200OK)]
    public IActionResult GetFailedJobs([FromQuery] int from = 0, [FromQuery] int count = 25)
    {
        return Ok(jobsFacade.GetFailedJobs(from, count));
    }

    [HttpGet("jobs/inspect/{jobId}")]
    [ProducesResponseType(typeof(AdminJobDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult GetJobDetail([FromRoute] string jobId)
    {
        if (string.IsNullOrWhiteSpace(jobId))
            return NotFound();

        var dto = jobsFacade.GetJobDetail(jobId.Trim());
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

        var dto = jobsFacade.GetJobDetail(jobId.Trim());
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

        var result = jobsFacade.RetryFailedJob(jobId);
        if (result.IsSuccess)
        {
            await WriteAuditAsync("jobs.failed.retry", "background-job", jobId, true, null, ct);
            return Ok(new { message = "Failed job re-queued.", jobId });
        }

        await WriteAuditAsync(
            "jobs.failed.retry",
            "background-job",
            jobId,
            false,
            new { error = result.Error },
            ct);
        return BadRequest(new { message = "Unable to re-queue failed job.", detail = result.Error });
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

        var outcome = jobsFacade.DeleteJob(normalizedId, expectedState);
        if (outcome.ThrewError is null && outcome.Result is not null)
        {
            await WriteAuditAsync(
                "jobs.delete",
                "background-job",
                normalizedId,
                outcome.Deleted,
                new { expectedState, currentState = outcome.CurrentState, request?.Reason, message = outcome.Message },
                ct);
            return Ok(outcome.Result);
        }

        await WriteAuditAsync(
            "jobs.delete",
            "background-job",
            normalizedId,
            false,
            new { expectedState, request?.Reason, error = outcome.ThrewError },
            ct);
        return BadRequest(new { message = "Unable to delete job.", detail = outcome.ThrewError });
    }

    [HttpPost("jobs/bulk-delete")]
    [EnableRateLimiting("admin-write")]
    [ProducesResponseType(typeof(AdminBulkDeleteJobsResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> BulkDeleteJobs([FromBody] AdminBulkDeleteJobsRequest request, CancellationToken ct)
    {
        var validation = AdminJobsFacade.ValidateBulkDeleteStates(request.States);
        if (!validation.IsValid)
        {
            return BadRequest(new
            {
                message = "Bulk delete only supports enqueued, scheduled, and failed states."
            });
        }

        var outcome = jobsFacade.BulkDeleteJobs(request);

        await WriteAuditAsync(
            "jobs.bulk-delete",
            "background-job",
            null,
            isSuccess: !request.DryRun ? outcome.Failed == 0 : true,
            metadata: new
            {
                states = outcome.States,
                request.Queues,
                request.JobType,
                request.Region,
                request.Query,
                request.OlderThanMinutes,
                limit = outcome.SafeLimit,
                scanLimit = outcome.SafeScanLimit,
                request.DryRun,
                matched = outcome.Matched,
                deleted = outcome.Deleted,
                failed = outcome.Failed,
                truncated = outcome.Truncated
            },
            ct: ct);

        return Ok(outcome.Result);
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
        var lookup = logsFacade.GetServiceLogs(service, level, q, sinceUtc, untilUtc, limit);
        if (!lookup.ServiceAllowed)
        {
            return BadRequest(new
            {
                message = "Unsupported service. Allowed values: webapi, service."
            });
        }

        return Ok(lookup.Response);
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
}
