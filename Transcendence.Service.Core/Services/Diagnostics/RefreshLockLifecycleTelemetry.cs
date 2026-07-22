using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Transcendence.Service.Core.Services.Jobs;

namespace Transcendence.Service.Core.Services.Diagnostics;

public interface IRefreshLockLifecycleTelemetry
{
    void RecordLifecycleOutcome(string lockKey, string outcome, string source);
    void RecordContentionWaitHint(string lockKey, int waitHintSeconds, string source);
    void RecordCleanupOutcome(
        int deletedCount,
        int batchesProcessed,
        bool stoppedByBatchCap,
        TimeSpan duration,
        string outcome,
        string source,
        string lockClass = RefreshLockLifecycleTelemetry.DefaultCleanupLockClass,
        string platformRegion = RefreshLockLifecycleTelemetry.DefaultGlobalPlatformRegion);

    void RecordGrowthSnapshot(
        int activeCount,
        int expiredCount,
        int deletedCount,
        string source,
        string lockClass = RefreshLockLifecycleTelemetry.DefaultCleanupLockClass,
        string platformRegion = RefreshLockLifecycleTelemetry.DefaultGlobalPlatformRegion);
}

public sealed class RefreshLockLifecycleTelemetry : IRefreshLockLifecycleTelemetry, IDisposable
{
    public const string DefaultCleanupLockClass = "refresh-lock-lifecycle";
    public const string DefaultGlobalPlatformRegion = "GLOBAL";

    private const string MeterName = "Transcendence.RefreshLocks";
    private const string MeterVersion = "1.0.0";
    private const string UnknownValue = "UNKNOWN";

    private readonly ILogger<RefreshLockLifecycleTelemetry> logger;
    private readonly Meter meter;
    private readonly Counter<long> lifecycleEventsCounter;
    private readonly Counter<long> cleanupDeletedCounter;
    private readonly Histogram<double> contentionWaitHintSeconds;
    private readonly Histogram<double> cleanupDurationMilliseconds;

    private GrowthSnapshot latestGrowthSnapshot = new(
        DefaultCleanupLockClass,
        DefaultGlobalPlatformRegion,
        ActiveCount: 0,
        ExpiredCount: 0,
        DeletedCount: 0);

    public RefreshLockLifecycleTelemetry(ILogger<RefreshLockLifecycleTelemetry> logger)
    {
        this.logger = logger;
        meter = new Meter(MeterName, MeterVersion);
        lifecycleEventsCounter = meter.CreateCounter<long>(
            "transcendence.refresh_lock.lifecycle.events",
            unit: "{event}",
            description: "Count of refresh-lock lifecycle outcomes by lock class, platform region, and outcome.");
        cleanupDeletedCounter = meter.CreateCounter<long>(
            "transcendence.refresh_lock.cleanup.deleted",
            unit: "{lock}",
            description: "Count of refresh-lock rows deleted during cleanup.");
        contentionWaitHintSeconds = meter.CreateHistogram<double>(
            "transcendence.refresh_lock.contention.wait_hint_seconds",
            unit: "s",
            description: "Suggested retry wait seconds emitted for lock contention.");
        cleanupDurationMilliseconds = meter.CreateHistogram<double>(
            "transcendence.refresh_lock.cleanup.duration_ms",
            unit: "ms",
            description: "Duration of refresh-lock cleanup runs.");

        meter.CreateObservableGauge<long>(
            "transcendence.refresh_lock.growth.active",
            ObserveActiveLocks,
            unit: "{lock}",
            description: "Latest active refresh-lock row count from lifecycle snapshots.");
        meter.CreateObservableGauge<long>(
            "transcendence.refresh_lock.growth.expired",
            ObserveExpiredLocks,
            unit: "{lock}",
            description: "Latest expired refresh-lock row count from lifecycle snapshots.");
        meter.CreateObservableGauge<long>(
            "transcendence.refresh_lock.growth.deleted_last_run",
            ObserveDeletedLocks,
            unit: "{lock}",
            description: "Latest deleted refresh-lock row count from lifecycle snapshots.");
    }

    public void RecordLifecycleOutcome(string lockKey, string outcome, string source)
    {
        EmitNonBlocking("refresh_lock.lifecycle", () =>
        {
            var dimensions = ResolveDimensions(lockKey);
            var normalizedOutcome = NormalizeToken(outcome, fallback: "unknown");
            var normalizedSource = NormalizeToken(source, fallback: "unknown");
            var tags = BuildTags(dimensions.LockClass, dimensions.PlatformRegion, normalizedOutcome, normalizedSource);

            lifecycleEventsCounter.Add(1, tags);
            logger.LogDebug(
                "event=refresh_lock.lifecycle lock_class={LockClass} platform_region={PlatformRegion} outcome={Outcome} source={Source} lock_key={LockKey}",
                dimensions.LockClass,
                dimensions.PlatformRegion,
                normalizedOutcome,
                normalizedSource,
                lockKey);
        });
    }

    public void RecordContentionWaitHint(string lockKey, int waitHintSeconds, string source)
    {
        EmitNonBlocking("refresh_lock.contention_wait_hint", () =>
        {
            var dimensions = ResolveDimensions(lockKey);
            var normalizedSource = NormalizeToken(source, fallback: "unknown");
            var normalizedWait = Math.Max(0, waitHintSeconds);
            var tags = BuildTags(dimensions.LockClass, dimensions.PlatformRegion, "contention", normalizedSource);

            contentionWaitHintSeconds.Record(normalizedWait, tags);
            logger.LogDebug(
                "event=refresh_lock.contention_wait_hint lock_class={LockClass} platform_region={PlatformRegion} outcome=contention source={Source} wait_hint_seconds={WaitHintSeconds} lock_key={LockKey}",
                dimensions.LockClass,
                dimensions.PlatformRegion,
                normalizedSource,
                normalizedWait,
                lockKey);
        });
    }

    public void RecordCleanupOutcome(
        int deletedCount,
        int batchesProcessed,
        bool stoppedByBatchCap,
        TimeSpan duration,
        string outcome,
        string source,
        string lockClass = DefaultCleanupLockClass,
        string platformRegion = DefaultGlobalPlatformRegion)
    {
        EmitNonBlocking("refresh_lock.cleanup", () =>
        {
            var normalizedOutcome = NormalizeToken(outcome, fallback: "unknown");
            var normalizedSource = NormalizeToken(source, fallback: "unknown");
            var normalizedLockClass = NormalizeToken(lockClass, fallback: DefaultCleanupLockClass);
            var normalizedPlatformRegion = NormalizePlatform(platformRegion);
            var normalizedDeletedCount = Math.Max(0, deletedCount);
            var normalizedBatches = Math.Max(0, batchesProcessed);
            var durationMilliseconds = Math.Max(0, duration.TotalMilliseconds);
            var tags = BuildTags(normalizedLockClass, normalizedPlatformRegion, normalizedOutcome, normalizedSource);

            lifecycleEventsCounter.Add(1, tags);
            cleanupDurationMilliseconds.Record(durationMilliseconds, tags);
            if (normalizedDeletedCount > 0)
                cleanupDeletedCounter.Add(normalizedDeletedCount, tags);

            logger.LogDebug(
                "event=refresh_lock.cleanup lock_class={LockClass} platform_region={PlatformRegion} outcome={Outcome} source={Source} deleted={DeletedCount} batches={BatchesProcessed} stopped_by_batch_cap={StoppedByBatchCap} duration_ms={DurationMilliseconds}",
                normalizedLockClass,
                normalizedPlatformRegion,
                normalizedOutcome,
                normalizedSource,
                normalizedDeletedCount,
                normalizedBatches,
                stoppedByBatchCap,
                durationMilliseconds);
        });
    }

    public void RecordGrowthSnapshot(
        int activeCount,
        int expiredCount,
        int deletedCount,
        string source,
        string lockClass = DefaultCleanupLockClass,
        string platformRegion = DefaultGlobalPlatformRegion)
    {
        EmitNonBlocking("refresh_lock.growth_snapshot", () =>
        {
            var normalizedSource = NormalizeToken(source, fallback: "unknown");
            var normalizedLockClass = NormalizeToken(lockClass, fallback: DefaultCleanupLockClass);
            var normalizedPlatformRegion = NormalizePlatform(platformRegion);
            var normalizedActiveCount = Math.Max(0, activeCount);
            var normalizedExpiredCount = Math.Max(0, expiredCount);
            var normalizedDeletedCount = Math.Max(0, deletedCount);
            var tags = BuildTags(normalizedLockClass, normalizedPlatformRegion, "snapshot", normalizedSource);

            lifecycleEventsCounter.Add(1, tags);
            if (normalizedDeletedCount > 0)
                cleanupDeletedCounter.Add(normalizedDeletedCount, tags);

            Volatile.Write(ref latestGrowthSnapshot, new GrowthSnapshot(
                normalizedLockClass,
                normalizedPlatformRegion,
                normalizedActiveCount,
                normalizedExpiredCount,
                normalizedDeletedCount));

            logger.LogDebug(
                "event=refresh_lock.growth_snapshot lock_class={LockClass} platform_region={PlatformRegion} outcome=snapshot source={Source} active_count={ActiveCount} expired_count={ExpiredCount} deleted_count={DeletedCount}",
                normalizedLockClass,
                normalizedPlatformRegion,
                normalizedSource,
                normalizedActiveCount,
                normalizedExpiredCount,
                normalizedDeletedCount);
        });
    }

    public void Dispose()
    {
        meter.Dispose();
    }

    private IEnumerable<Measurement<long>> ObserveActiveLocks()
    {
        var snapshot = Volatile.Read(ref latestGrowthSnapshot);
        var tags = BuildTags(snapshot.LockClass, snapshot.PlatformRegion, "active", "lifecycle-job");
        yield return new Measurement<long>(snapshot.ActiveCount, tags);
    }

    private IEnumerable<Measurement<long>> ObserveExpiredLocks()
    {
        var snapshot = Volatile.Read(ref latestGrowthSnapshot);
        var tags = BuildTags(snapshot.LockClass, snapshot.PlatformRegion, "expired", "lifecycle-job");
        yield return new Measurement<long>(snapshot.ExpiredCount, tags);
    }

    private IEnumerable<Measurement<long>> ObserveDeletedLocks()
    {
        var snapshot = Volatile.Read(ref latestGrowthSnapshot);
        var tags = BuildTags(snapshot.LockClass, snapshot.PlatformRegion, "deleted_last_run", "lifecycle-job");
        yield return new Measurement<long>(snapshot.DeletedCount, tags);
    }

    private void EmitNonBlocking(string eventName, Action emit)
    {
        try
        {
            emit();
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "event=refresh_lock.telemetry_failure telemetry_event={TelemetryEvent} outcome=ignored",
                eventName);
        }
    }

    private static LockDimensions ResolveDimensions(string lockKey)
    {
        if (string.IsNullOrWhiteSpace(lockKey))
            return new LockDimensions("unknown", UnknownValue);

        var trimmed = lockKey.Trim();
        if (trimmed.StartsWith(RefreshLockKeys.SummonerRefreshPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var platformRegion = ExtractPlatform(trimmed[RefreshLockKeys.SummonerRefreshPrefix.Length..]);
            return new LockDimensions("summoner-refresh", platformRegion);
        }

        if (trimmed.StartsWith(RefreshLockKeys.ApiPriorityRefreshPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var platformRegion = ExtractPlatform(trimmed[RefreshLockKeys.ApiPriorityRefreshPrefix.Length..]);
            return new LockDimensions("refresh-priority:api", platformRegion);
        }

        var parts = trimmed.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
            return new LockDimensions("unknown", UnknownValue);

        var lockClass = NormalizeToken(parts[0], fallback: "unknown");
        var platform = parts.Length > 1 ? NormalizePlatform(parts[1]) : UnknownValue;
        return new LockDimensions(lockClass, platform);
    }

    private static string ExtractPlatform(string suffix)
    {
        if (string.IsNullOrWhiteSpace(suffix))
            return UnknownValue;

        var delimiter = suffix.IndexOf(':');
        var platform = delimiter < 0 ? suffix : suffix[..delimiter];
        return NormalizePlatform(platform);
    }

    private static string NormalizePlatform(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? UnknownValue : value.Trim().ToUpperInvariant();
    }

    private static string NormalizeToken(string value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;

        return value.Trim().ToLowerInvariant();
    }

    private static TagList BuildTags(string lockClass, string platformRegion, string outcome, string source)
    {
        return new TagList
        {
            { "lock_class", lockClass },
            { "platform_region", platformRegion },
            { "outcome", outcome },
            { "source", source }
        };
    }

    private readonly record struct LockDimensions(string LockClass, string PlatformRegion);
    private sealed record GrowthSnapshot(
        string LockClass,
        string PlatformRegion,
        long ActiveCount,
        long ExpiredCount,
        long DeletedCount);
}
