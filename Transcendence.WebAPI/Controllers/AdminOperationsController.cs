using System.Security.Claims;
using System.Text.Json;
using Hangfire;
using Hangfire.Storage;
using Hangfire.Storage.Monitoring;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Transcendence.Data;
using Transcendence.Service.Core.Services.Analytics.Interfaces;
using Transcendence.Service.Core.Services.Auth.Interfaces;
using Transcendence.Service.Core.Services.Auth.Models;
using Transcendence.Service.Core.Services.Diagnostics;
using Transcendence.WebAPI.Security;

namespace Transcendence.WebAPI.Controllers;

[ApiController]
[Route("api/admin")]
[Authorize(Policy = AuthPolicies.AdminOnly)]
public class AdminOperationsController(
    JobStorage jobStorage,
    IConfiguration configuration,
    TranscendenceContext db,
    IChampionAnalyticsService analyticsService,
    IAdminAuditService adminAuditService) : ControllerBase
{
    private static readonly HashSet<string> AllowedServiceLogKeys = ["webapi", "service"];

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

    [HttpGet("jobs/failed/{jobId}")]
    [ProducesResponseType(typeof(AdminFailedJobDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult GetFailedJobDetail([FromRoute] string jobId)
    {
        if (string.IsNullOrWhiteSpace(jobId))
            return NotFound();

        var safeJobId = jobId.Trim();
        var monitoring = jobStorage.GetMonitoringApi();
        var details = monitoring.JobDetails(safeJobId);
        if (details is null)
            return NotFound(new { message = "Job not found.", jobId = safeJobId });

        var history = details.History ?? [];
        var states = history
            .Select(MapStateTransition)
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToList();

        var failedState = states.FirstOrDefault(x => string.Equals(x.StateName, "Failed", StringComparison.OrdinalIgnoreCase));
        var exceptionType = failedState?.Data.GetValueOrDefault("ExceptionType");
        var exceptionMessage = failedState?.Data.GetValueOrDefault("ExceptionMessage");
        var exceptionDetails = failedState?.Data.GetValueOrDefault("ExceptionDetails");

        var dto = new AdminFailedJobDetailDto(
            JobId: safeJobId,
            JobType: details.Job?.Type?.FullName,
            JobMethod: details.Job?.Method?.Name,
            Arguments: details.Job?.Args?.Select(SafeSerialize).ToList() ?? [],
            FailedAtUtc: failedState?.CreatedAtUtc,
            CurrentState: states.FirstOrDefault()?.StateName,
            Reason: failedState?.Reason,
            ExceptionType: exceptionType,
            ExceptionMessage: exceptionMessage,
            ExceptionDetails: exceptionDetails,
            FailedCount: states.Count(x => string.Equals(x.StateName, "Failed", StringComparison.OrdinalIgnoreCase)),
            States: states,
            Properties: details.Properties is null
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : new Dictionary<string, string>(details.Properties, StringComparer.Ordinal)
        );

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

    [HttpGet("logs/services")]
    [ProducesResponseType(typeof(IReadOnlyList<AdminServiceLogDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public IActionResult GetServiceLogs(
        [FromQuery] string service = "service",
        [FromQuery] string? level = null,
        [FromQuery] string? q = null,
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
        var configuredDirectory = configuration["OperationalLogs:DirectoryPath"];
        var directory = string.IsNullOrWhiteSpace(configuredDirectory)
            ? Path.Combine(AppContext.BaseDirectory, "logs")
            : configuredDirectory;
        var path = Path.Combine(directory, $"{serviceKey}.log");

        if (!System.IO.File.Exists(path))
            return Ok(Array.Empty<AdminServiceLogDto>());

        var normalizedLevel = string.IsNullOrWhiteSpace(level)
            ? null
            : level.Trim().ToUpperInvariant();
        var search = string.IsNullOrWhiteSpace(q) ? null : q.Trim();

        var entries = new List<AdminServiceLogDto>(safeLimit);
        foreach (var line in ReadMostRecentLines(path, maxLines: 4000))
        {
            if (!TryParseOperationalLogLine(line, out var parsed))
                continue;

            if (normalizedLevel is not null && !string.Equals(parsed.Level, normalizedLevel, StringComparison.OrdinalIgnoreCase))
                continue;

            if (search is not null &&
                !ContainsSearch(parsed.Message, search) &&
                !ContainsSearch(parsed.Category, search) &&
                !ContainsSearch(parsed.Exception, search))
                continue;

            entries.Add(parsed);
            if (entries.Count >= safeLimit)
                break;
        }

        return Ok(entries);
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
        var queue = new Queue<string>(maxLines);
        foreach (var line in System.IO.File.ReadLines(path))
        {
            queue.Enqueue(line);
            if (queue.Count > maxLines)
                queue.Dequeue();
        }

        return queue.Reverse();
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

public record AdminFailedJobDetailDto(
    string JobId,
    string? JobType,
    string? JobMethod,
    IReadOnlyList<string> Arguments,
    DateTime? FailedAtUtc,
    string? CurrentState,
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

public record AdminServiceLogDto(
    DateTime TimestampUtc,
    string Service,
    string Level,
    string Category,
    int EventId,
    string? Message,
    string? Exception
);
