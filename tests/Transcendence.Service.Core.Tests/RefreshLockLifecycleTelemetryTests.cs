using System.Diagnostics.Metrics;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Transcendence.Service.Core.Services.Diagnostics;

namespace Transcendence.Service.Core.Tests;

public class RefreshLockLifecycleTelemetryTests
{
    [Fact]
    public void RecordLifecycleOutcome_EmitsExpectedTags_ForSummonerRefreshLock()
    {
        using var telemetry = new RefreshLockLifecycleTelemetry(Mock.Of<ILogger<RefreshLockLifecycleTelemetry>>());
        using var listener = new MeterListener();

        var lifecycleMeasurements = new List<RecordedLongMeasurement>();
        ConfigureListener(
            listener,
            onLong: (instrumentName, value, tags) =>
            {
                if (instrumentName == "transcendence.refresh_lock.lifecycle.events")
                    lifecycleMeasurements.Add(new RecordedLongMeasurement(value, tags));
            });

        telemetry.RecordLifecycleOutcome("summoner-refresh:NA1:NAME:TAG", "acquired", "unit-test");

        lifecycleMeasurements.Should().ContainSingle();
        var measurement = lifecycleMeasurements[0];
        measurement.Value.Should().Be(1);
        measurement.Tag("lock_class").Should().Be("summoner-refresh");
        measurement.Tag("platform_region").Should().Be("NA1");
        measurement.Tag("outcome").Should().Be("acquired");
        measurement.Tag("source").Should().Be("unit-test");
    }

    [Fact]
    public void CleanupAndGrowthSnapshot_PublishDurationDeletedAndObservableCounts()
    {
        using var telemetry = new RefreshLockLifecycleTelemetry(Mock.Of<ILogger<RefreshLockLifecycleTelemetry>>());
        using var listener = new MeterListener();

        var cleanupDurations = new List<RecordedDoubleMeasurement>();
        var deletedCounters = new List<RecordedLongMeasurement>();
        var activeGauges = new List<RecordedLongMeasurement>();
        var expiredGauges = new List<RecordedLongMeasurement>();
        var deletedGauges = new List<RecordedLongMeasurement>();

        ConfigureListener(
            listener,
            onLong: (instrumentName, value, tags) =>
            {
                switch (instrumentName)
                {
                    case "transcendence.refresh_lock.cleanup.deleted":
                        deletedCounters.Add(new RecordedLongMeasurement(value, tags));
                        break;
                    case "transcendence.refresh_lock.growth.active":
                        activeGauges.Add(new RecordedLongMeasurement(value, tags));
                        break;
                    case "transcendence.refresh_lock.growth.expired":
                        expiredGauges.Add(new RecordedLongMeasurement(value, tags));
                        break;
                    case "transcendence.refresh_lock.growth.deleted_last_run":
                        deletedGauges.Add(new RecordedLongMeasurement(value, tags));
                        break;
                }
            },
            onDouble: (instrumentName, value, tags) =>
            {
                if (instrumentName == "transcendence.refresh_lock.cleanup.duration_ms")
                    cleanupDurations.Add(new RecordedDoubleMeasurement(value, tags));
            });

        telemetry.RecordCleanupOutcome(
            deletedCount: 7,
            batchesProcessed: 2,
            stoppedByBatchCap: false,
            duration: TimeSpan.FromMilliseconds(125),
            outcome: "completed",
            source: "unit-test");
        telemetry.RecordGrowthSnapshot(
            activeCount: 11,
            expiredCount: 4,
            deletedCount: 7,
            source: "unit-test");
        listener.RecordObservableInstruments();

        cleanupDurations.Should().ContainSingle();
        cleanupDurations[0].Value.Should().BeGreaterThanOrEqualTo(125);
        cleanupDurations[0].Tag("lock_class").Should().Be("refresh-lock-lifecycle");
        cleanupDurations[0].Tag("platform_region").Should().Be("GLOBAL");
        cleanupDurations[0].Tag("outcome").Should().Be("completed");

        deletedCounters.Should().NotBeEmpty();
        activeGauges.Should().ContainSingle();
        activeGauges[0].Value.Should().Be(11);
        activeGauges[0].Tag("outcome").Should().Be("active");
        activeGauges[0].Tag("platform_region").Should().Be("GLOBAL");

        expiredGauges.Should().ContainSingle();
        expiredGauges[0].Value.Should().Be(4);
        expiredGauges[0].Tag("outcome").Should().Be("expired");

        deletedGauges.Should().ContainSingle();
        deletedGauges[0].Value.Should().Be(7);
        deletedGauges[0].Tag("outcome").Should().Be("deleted_last_run");
    }

    private static void ConfigureListener(
        MeterListener listener,
        Action<string, long, Dictionary<string, string?>> onLong,
        Action<string, double, Dictionary<string, string?>>? onDouble = null)
    {
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == "Transcendence.RefreshLocks")
                meterListener.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            onLong(instrument.Name, value, ToDictionary(tags)));
        if (onDouble != null)
        {
            listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
                onDouble(instrument.Name, value, ToDictionary(tags)));
        }

        listener.Start();
    }

    private static Dictionary<string, string?> ToDictionary(ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        var result = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var tag in tags)
            result[tag.Key] = tag.Value?.ToString();

        return result;
    }

    private sealed record RecordedLongMeasurement(long Value, Dictionary<string, string?> Tags)
    {
        public string? Tag(string key)
        {
            return Tags.TryGetValue(key, out var value) ? value : null;
        }
    }

    private sealed record RecordedDoubleMeasurement(double Value, Dictionary<string, string?> Tags)
    {
        public string? Tag(string key)
        {
            return Tags.TryGetValue(key, out var value) ? value : null;
        }
    }
}
