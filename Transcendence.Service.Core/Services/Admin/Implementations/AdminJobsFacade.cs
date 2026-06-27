using System.Text.Json;
using Hangfire;
using Hangfire.Common;
using Hangfire.Storage;
using Hangfire.Storage.Monitoring;
using Microsoft.Extensions.Options;
using Transcendence.Service.Core.Services.Admin.Interfaces;
using Transcendence.Service.Core.Services.Jobs.Configuration;
using Transcendence.WebAPI.Controllers;

namespace Transcendence.Service.Core.Services.Admin.Implementations;

/// <summary>
/// Hosts the verbatim job-storage/Hangfire logic extracted from AdminOperationsController's
/// <c>jobs/*</c> actions (P10.1). Behavior-preserving: the controller still maps results to
/// IActionResult and records audit events.
/// </summary>
public sealed class AdminJobsFacade(
    JobStorage jobStorage,
    IOptions<WorkerJobScheduleOptions> workerScheduleOptions,
    IOptions<MultiRegionIngestionOptions> multiRegionOptions,
    IRecurringJobManager recurringJobManager,
    IWorkerRecurringJobPolicy recurringJobPolicy) : IAdminJobsFacade
{
    private const int DefaultJobScanLimit = 5000;
    private const int DefaultJobScanPageSize = 250;
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

    public IReadOnlyList<AdminRecurringJobDto> GetRecurringJobs()
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

        return jobs;
    }

    public AdminMutationResult TriggerRecurring(string id)
    {
        try
        {
            RecurringJob.TriggerJob(id.Trim());
            return new AdminMutationResult(true, null);
        }
        catch (Exception ex)
        {
            return new AdminMutationResult(false, ex.Message);
        }
    }

    /// <summary>
    /// Returns true when the recurring job id is one that admin is allowed to pause/resume.
    /// </summary>
    public static bool IsPausableRecurringJob(string normalizedId) =>
        PausableRecurringJobIds.Contains(normalizedId);

    public AdminMutationResult PauseRecurring(string id)
    {
        var normalizedId = id.Trim();
        try
        {
            RecurringJob.RemoveIfExists(normalizedId);
            return new AdminMutationResult(true, null);
        }
        catch (Exception ex)
        {
            return new AdminMutationResult(false, ex.Message);
        }
    }

    /// <summary>
    /// Resolves the recurring-job descriptor for an admin resume request, returning a discriminated
    /// validation outcome that the controller maps to the original BadRequest responses.
    /// </summary>
    public AdminResumeValidation ValidateResume(string normalizedId)
    {
        var descriptor = recurringJobPolicy
            .BuildDescriptors(workerScheduleOptions.Value)
            .FirstOrDefault(x => string.Equals(x.JobId, normalizedId, StringComparison.OrdinalIgnoreCase));
        if (descriptor is null || !PausableRecurringJobIds.Contains(normalizedId))
            return new AdminResumeValidation(AdminResumeValidationKind.NotPausable, null);
        if (!descriptor.IsEnabled)
            return new AdminResumeValidation(AdminResumeValidationKind.DisabledByConfiguration, null);

        return new AdminResumeValidation(AdminResumeValidationKind.Ok, descriptor);
    }

    public AdminMutationResult ResumeRecurring(string id)
    {
        var normalizedId = id.Trim();
        var validation = ValidateResume(normalizedId);
        if (validation.Kind != AdminResumeValidationKind.Ok || validation.Descriptor is null)
        {
            // The controller is expected to short-circuit on validation; defensively mirror failure.
            return new AdminMutationResult(false, null);
        }

        try
        {
            validation.Descriptor.Apply(recurringJobManager);
            return new AdminMutationResult(true, null);
        }
        catch (Exception ex)
        {
            return new AdminMutationResult(false, ex.Message);
        }
    }

    public AdminQueueSummaryResponse GetQueueSummary(int scanLimit)
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

        return new AdminQueueSummaryResponse(
            GeneratedAtUtc: DateTime.UtcNow,
            Truncated: truncated,
            ScanLimit: safeScanLimit,
            Queues: monitoring.Queues()
                .Select(q => new AdminQueueSnapshot(q.Name, q.Length, q.Fetched))
                .OrderByDescending(x => x.Length)
                .ToList(),
            TopGroups: groups);
    }

    public AdminJobListLookup GetJobs(
        string state,
        string? queue,
        string? type,
        string? region,
        string? q,
        int? olderThanMinutes,
        int from,
        int count,
        int scanLimit)
    {
        var normalizedStates = NormalizeRequestedStates([state]);
        if (normalizedStates.Count == 0)
            return new AdminJobListLookup(false, null);

        var safeFrom = Math.Max(0, from);
        var safeCount = Math.Clamp(count, 1, 200);
        var safeScanLimit = Math.Clamp(scanLimit, 100, 50000);
        var monitoring = jobStorage.GetMonitoringApi();
        var jobs = ScanJobs(monitoring, normalizedStates, safeScanLimit, out var truncated);
        var filtered = ApplyJobFilters(jobs, queue, type, region, q, olderThanMinutes).ToList();
        var page = filtered.Skip(safeFrom).Take(safeCount).ToList();

        return new AdminJobListLookup(true, new AdminJobListResponse(
            GeneratedAtUtc: DateTime.UtcNow,
            From: safeFrom,
            Count: safeCount,
            TotalMatched: filtered.Count,
            Truncated: truncated,
            ScanLimit: safeScanLimit,
            Items: page));
    }

    public IReadOnlyList<AdminFailedJobDto> GetFailedJobs(int from, int count)
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

        return failed;
    }

    public AdminJobDetailDto? GetJobDetail(string safeJobId) => BuildJobDetail(safeJobId);

    public AdminMutationResult RetryFailedJob(string jobId)
    {
        try
        {
            BackgroundJob.Requeue(jobId.Trim());
            return new AdminMutationResult(true, null);
        }
        catch (Exception ex)
        {
            return new AdminMutationResult(false, ex.Message);
        }
    }

    public AdminDeleteJobOutcome DeleteJob(string normalizedId, string? expectedState)
    {
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
            return new AdminDeleteJobOutcome(
                Result: new AdminDeleteJobResultDto
                {
                    JobId = normalizedId,
                    Deleted = deleted,
                    ExpectedState = expectedState,
                    CurrentState = currentState,
                    Message = message
                },
                Deleted: deleted,
                ExpectedState: expectedState,
                CurrentState: currentState,
                Message: message,
                ThrewError: null);
        }
        catch (Exception ex)
        {
            return new AdminDeleteJobOutcome(
                Result: null,
                Deleted: false,
                ExpectedState: expectedState,
                CurrentState: null,
                Message: null,
                ThrewError: ex.Message);
        }
    }

    public AdminBulkDeleteJobsOutcome BulkDeleteJobs(AdminBulkDeleteJobsRequest request)
    {
        var states = NormalizeRequestedStates(request.States?.Count > 0 ? request.States : ["enqueued", "scheduled", "failed"]);
        // The controller validates state-allowance before calling; defensively keep the same normalization.

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

        return new AdminBulkDeleteJobsOutcome(
            Result: new AdminBulkDeleteJobsResultDto(
                DryRun: request.DryRun,
                Truncated: truncated,
                Matched: candidates.Count,
                Deleted: deleted,
                Failed: failed,
                SampleJobIds: candidates.Take(20).Select(x => x.JobId).ToList()),
            States: states,
            SafeLimit: safeLimit,
            SafeScanLimit: safeScanLimit,
            Matched: candidates.Count,
            Deleted: deleted,
            Failed: failed,
            Truncated: truncated);
    }

    /// <summary>
    /// Validates the requested bulk-delete states against the allowed set, returning the normalized
    /// states (so the controller can reject before mutating, exactly like the original action).
    /// </summary>
    public static AdminBulkDeleteValidation ValidateBulkDeleteStates(IReadOnlyList<string>? requestStates)
    {
        var states = NormalizeRequestedStates(requestStates?.Count > 0 ? requestStates : ["enqueued", "scheduled", "failed"]);
        var isValid = states.Count != 0 && states.All(x => BulkDeletableStates.Contains(x));
        return new AdminBulkDeleteValidation(isValid, states);
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
}
