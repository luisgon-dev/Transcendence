using Transcendence.Service.Core.Services.Admin.Models;
using Transcendence.Service.Core.Services.Jobs.Configuration;

namespace Transcendence.Service.Core.Services.Admin.Interfaces;

/// <summary>
/// Encapsulates all Hangfire/job-storage logic that previously lived directly in
/// AdminOperationsController's <c>jobs/*</c> actions. The facade owns JobStorage, the recurring
/// job manager/policy and the worker schedule options, and returns plain result/DTO types so the
/// thin controller keeps ownership of HTTP status-code mapping and audit-event recording.
/// Behavior is preserved verbatim from the original controller.
/// </summary>
public interface IAdminJobsFacade
{
    IReadOnlyList<AdminRecurringJobDto> GetRecurringJobs();

    AdminMutationResult TriggerRecurring(string id);

    AdminMutationResult PauseRecurring(string id);

    AdminResumeValidation ValidateResume(string normalizedId);

    AdminMutationResult ResumeRecurring(string id);

    AdminQueueSummaryResponse GetQueueSummary(int scanLimit);

    AdminJobListLookup GetJobs(
        string state,
        string? queue,
        string? type,
        string? region,
        string? q,
        int? olderThanMinutes,
        int from,
        int count,
        int scanLimit);

    IReadOnlyList<AdminFailedJobDto> GetFailedJobs(int from, int count);

    AdminJobDetailDto? GetJobDetail(string safeJobId);

    AdminMutationResult RetryFailedJob(string jobId);

    AdminDeleteJobOutcome DeleteJob(string normalizedId, string? expectedState);

    AdminBulkDeleteJobsOutcome BulkDeleteJobs(AdminBulkDeleteJobsRequest request);
}

/// <summary>
/// Result of a guarded mutating recurring/background job operation. Mirrors the original
/// controller's try/catch: on success <see cref="IsSuccess"/> is true and <see cref="Error"/> is
/// null; on failure it carries the exception message so the controller can build the identical
/// BadRequest payload and failure audit metadata.
/// </summary>
public sealed record AdminMutationResult(bool IsSuccess, string? Error);

/// <summary>
/// The normalized state-list outcome for <c>jobs/list</c>. When <see cref="StatesValid"/> is false
/// the requested state could not be normalized and the controller must return the original
/// 400 response. Otherwise <see cref="Response"/> holds the populated list payload.
/// </summary>
public sealed record AdminJobListLookup(bool StatesValid, AdminJobListResponse? Response);

/// <summary>
/// Outcome of a single-job delete. Carries the result DTO and the audit metadata pieces the
/// controller needs to reproduce the original audit write (deleted flag, expected/current state,
/// message). On exception <see cref="ThrewError"/> holds the message for the failure path.
/// </summary>
public sealed record AdminDeleteJobOutcome(
    AdminDeleteJobResultDto? Result,
    bool Deleted,
    string? ExpectedState,
    string? CurrentState,
    string? Message,
    string? ThrewError);

/// <summary>
/// Outcome of a bulk delete. Carries the result DTO plus the normalized states and counters that
/// the controller writes into the audit metadata, preserving the original shape exactly.
/// </summary>
public sealed record AdminBulkDeleteJobsOutcome(
    AdminBulkDeleteJobsResultDto Result,
    IReadOnlyCollection<string> States,
    int SafeLimit,
    int SafeScanLimit,
    int Matched,
    int Deleted,
    int Failed,
    bool Truncated);

public enum AdminResumeValidationKind
{
    Ok,
    NotPausable,
    DisabledByConfiguration
}

/// <summary>
/// Discriminated outcome of resume-request validation, mapped by the controller to the original
/// BadRequest responses (or to the success path that applies the descriptor).
/// </summary>
public sealed record AdminResumeValidation(AdminResumeValidationKind Kind, WorkerRecurringJobDescriptor? Descriptor);

/// <summary>
/// Outcome of bulk-delete state validation: whether the requested states are allowed and the
/// normalized state set (so the controller can reproduce the original BadRequest exactly).
/// </summary>
public sealed record AdminBulkDeleteValidation(bool IsValid, IReadOnlyCollection<string> States);
