using System.Security.Claims;
using Hangfire;
using Hangfire.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Transcendence.Data;
using Transcendence.Service.Core.Services.Analytics.Interfaces;
using Transcendence.Service.Core.Services.Auth.Interfaces;
using Transcendence.Service.Core.Services.Auth.Models;
using Transcendence.WebAPI.Security;

namespace Transcendence.WebAPI.Controllers;

[ApiController]
[Route("api/admin")]
[Authorize(Policy = AuthPolicies.AdminOnly)]
public class AdminOperationsController(
    JobStorage jobStorage,
    TranscendenceContext db,
    IChampionAnalyticsService analyticsService,
    IAdminAuditService adminAuditService) : ControllerBase
{
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
            queues));
    }

    [HttpGet("jobs/recurring")]
    [ProducesResponseType(typeof(IReadOnlyList<AdminRecurringJobDto>), StatusCodes.Status200OK)]
    public IActionResult GetRecurringJobs()
    {
        using var connection = jobStorage.GetConnection();
        var jobs = connection.GetRecurringJobs()
            .Select(j => new AdminRecurringJobDto(
                j.Id,
                j.Queue,
                j.Cron,
                j.NextExecution,
                j.LastExecution,
                j.LastJobId,
                j.LastJobState,
                j.Error))
            .OrderBy(x => x.Id, StringComparer.Ordinal)
            .ToList();

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
            await WriteAuditAsync(
                "jobs.recurring.trigger",
                targetType: "recurring-job",
                targetId: id,
                isSuccess: true,
                metadata: null,
                ct: ct);
            return Ok(new { message = "Recurring job triggered.", id });
        }
        catch (Exception ex)
        {
            await WriteAuditAsync(
                "jobs.recurring.trigger",
                targetType: "recurring-job",
                targetId: id,
                isSuccess: false,
                metadata: new { error = ex.Message },
                ct: ct);
            return BadRequest(new { message = "Unable to trigger recurring job.", detail = ex.Message });
        }
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
            await WriteAuditAsync(
                "jobs.failed.retry",
                targetType: "background-job",
                targetId: jobId,
                isSuccess: true,
                metadata: null,
                ct: ct);
            return Ok(new { message = "Failed job re-queued.", jobId });
        }
        catch (Exception ex)
        {
            await WriteAuditAsync(
                "jobs.failed.retry",
                targetType: "background-job",
                targetId: jobId,
                isSuccess: false,
                metadata: new { error = ex.Message },
                ct: ct);
            return BadRequest(new { message = "Unable to re-queue failed job.", detail = ex.Message });
        }
    }

    [HttpPost("cache/invalidate")]
    [EnableRateLimiting("admin-write")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> InvalidateAnalyticsCache(CancellationToken ct)
    {
        await analyticsService.InvalidateAnalyticsCacheAsync(ct);
        await WriteAuditAsync(
            "cache.invalidate",
            targetType: "analytics-cache",
            targetId: null,
            isSuccess: true,
            metadata: null,
            ct: ct);
        return Ok(new { message = "Analytics cache invalidated." });
    }

    [HttpGet("audit-log")]
    [ProducesResponseType(typeof(IReadOnlyList<AdminAuditEntryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAuditLog([FromQuery] int limit = 100, CancellationToken ct = default)
    {
        var rows = await adminAuditService.ListRecentAsync(limit, ct);
        return Ok(rows);
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

public record AdminOverviewResponse(
    DateTime GeneratedAtUtc,
    bool DatabaseConnected,
    long Enqueued,
    long Processing,
    long Scheduled,
    long Failed,
    long Succeeded,
    long Recurring,
    IReadOnlyList<AdminQueueSnapshot> Queues
);

public record AdminQueueSnapshot(string Name, long Length, long? Fetched);

public record AdminRecurringJobDto(
    string Id,
    string Queue,
    string Cron,
    DateTime? NextExecution,
    DateTime? LastExecution,
    string? LastJobId,
    string? LastJobState,
    string? Error
);

public record AdminFailedJobDto(
    string JobId,
    string? Reason,
    string? ExceptionType,
    string? ExceptionMessage,
    DateTime? FailedAt
);
