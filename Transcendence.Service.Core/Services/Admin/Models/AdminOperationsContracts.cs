using System.ComponentModel.DataAnnotations;

// NOTE: These admin DTO contracts intentionally live in the Transcendence.WebAPI.Controllers
// namespace so that the public OpenAPI schema names and the controller's call sites remain
// byte-for-byte unchanged after the controller was decomposed behind facades (P10.1).
// They are physically hosted in Transcendence.Service.Core so the admin facades (which live in
// Service.Core) can return them while the WebAPI controller still maps them to IActionResult.
namespace Transcendence.WebAPI.Controllers;

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
