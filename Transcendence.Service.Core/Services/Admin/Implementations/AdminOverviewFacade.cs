using System.Text.Json;
using Hangfire;
using Hangfire.Common;
using Hangfire.Storage;
using Hangfire.Storage.Monitoring;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Transcendence.Data;
using Transcendence.Data.Models.LoL.Match;
using Transcendence.Service.Core.Services.Admin.Interfaces;
using Transcendence.Service.Core.Services.Jobs.Configuration;
using Transcendence.Service.Core.Services.RiotApi;
using Transcendence.WebAPI.Controllers;

namespace Transcendence.Service.Core.Services.Admin.Implementations;

/// <summary>
/// Hosts the verbatim overview/metrics snapshot logic extracted from AdminOperationsController
/// (P10.1). Behavior-preserving: shapes/values are identical to the original actions.
/// </summary>
public sealed class AdminOverviewFacade(
    JobStorage jobStorage,
    IOptions<ChampionAnalyticsIngestionJobOptions> ingestionOptions,
    IOptions<MultiRegionIngestionOptions> multiRegionOptions,
    TranscendenceContext db) : IAdminOverviewFacade
{
    private const int DefaultJobScanPageSize = 250;

    private static readonly string[] KnownPlatformRegions =
    [
        "NA1", "EUW1", "EUN1", "KR", "BR1", "JP1", "LA1", "LA2", "OC1", "TR1", "RU", "PH2", "SG2", "TH2", "TW2",
        "VN2"
    ];

    public async Task<AdminOverviewResponse> GetOverviewAsync(CancellationToken ct)
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
        return new AdminOverviewResponse(
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
            queues);
    }

    public async Task<AdminAnalysisMetricsResponse> GetAnalysisMetricsAsync(CancellationToken ct)
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

        return new AdminAnalysisMetricsResponse(
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
            Regions: regions);
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

    private static string NormalizeRegionKey(string? region)
    {
        return string.IsNullOrWhiteSpace(region) ? "UNKNOWN" : region.Trim().ToUpperInvariant();
    }

    private static string? GetDictionaryValue(IDictionary<string, string>? data, string key)
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
