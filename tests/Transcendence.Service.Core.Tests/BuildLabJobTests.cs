using System.Diagnostics.Metrics;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Transcendence.Service.Core.Services.Analytics.Interfaces;
using Transcendence.Service.Core.Services.Diagnostics;
using Transcendence.Service.Core.Services.Jobs;

namespace Transcendence.Service.Core.Tests;

/// <summary>
/// A <see cref="MeterListener"/> can only filter by meter name, so every test that instantiates
/// <see cref="BuildLabTelemetry"/> shares this collection and runs sequentially. Otherwise a
/// concurrent class emitting on the same meter would leak measurements into these assertions.
/// </summary>
[CollectionDefinition(BuildLabTelemetryCollection.Name, DisableParallelization = true)]
public sealed class BuildLabTelemetryCollection
{
    public const string Name = "build-lab-telemetry";
}

[Collection(BuildLabTelemetryCollection.Name)]
public sealed class BuildLabJobTests
{
    [Fact]
    public async Task CreateJob_RecordsASkippedTickWhenNoGenerationWasCreated()
    {
        var coordinator = new Mock<IBuildLabGenerationCoordinator>();
        coordinator
            .Setup(instance => instance.CreatePendingGenerationAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid?)null);
        using var telemetry = new BuildLabTelemetry();
        using var events = new GenerationEventRecorder();

        await new CreateBuildLabGenerationJob(
            coordinator.Object, telemetry, NullLogger<CreateBuildLabGenerationJob>.Instance)
            .ExecuteAsync(CancellationToken.None);

        // "Nothing to do" has to be distinguishable from "the daily job is dead".
        events.Measurements.Should().BeEquivalentTo([("create", "skipped")]);
    }

    [Fact]
    public async Task CreateJob_RecordsACreationFailureAndRethrows()
    {
        var coordinator = new Mock<IBuildLabGenerationCoordinator>();
        coordinator
            .Setup(instance => instance.CreatePendingGenerationAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("cohort scan timed out"));
        using var telemetry = new BuildLabTelemetry();
        using var events = new GenerationEventRecorder();
        var job = new CreateBuildLabGenerationJob(
            coordinator.Object, telemetry, NullLogger<CreateBuildLabGenerationJob>.Instance);

        var act = () => job.ExecuteAsync(CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        events.Measurements.Should().BeEquivalentTo([("create", "error")]);
    }

    [Fact]
    public async Task CreateJob_RecordsNothingItselfWhenAGenerationWasCreated()
    {
        var coordinator = new Mock<IBuildLabGenerationCoordinator>();
        coordinator
            .Setup(instance => instance.CreatePendingGenerationAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());
        using var telemetry = new BuildLabTelemetry();
        using var events = new GenerationEventRecorder();

        await new CreateBuildLabGenerationJob(
            coordinator.Object, telemetry, NullLogger<CreateBuildLabGenerationJob>.Instance)
            .ExecuteAsync(CancellationToken.None);

        // The coordinator emits the success event, so the job must not double-count it.
        events.Measurements.Should().BeEmpty();
    }

    [Fact]
    public async Task PromoteJob_RecordsAPromotionFailureAndRethrows()
    {
        var coordinator = new Mock<IBuildLabGenerationCoordinator>();
        coordinator
            .Setup(instance => instance.PromoteReadyCandidatesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("lease reaper failed"));
        using var telemetry = new BuildLabTelemetry();
        using var events = new GenerationEventRecorder();
        var job = new PromoteBuildLabGenerationJob(
            coordinator.Object, telemetry, NullLogger<PromoteBuildLabGenerationJob>.Instance);

        var act = () => job.ExecuteAsync(CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        events.Measurements.Should().BeEquivalentTo([("promote", "error")]);
    }

    [Fact]
    public async Task PromoteJob_RecordsNothingItselfOnASuccessfulTick()
    {
        var coordinator = new Mock<IBuildLabGenerationCoordinator>();
        coordinator
            .Setup(instance => instance.PromoteReadyCandidatesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(2);
        using var telemetry = new BuildLabTelemetry();
        using var events = new GenerationEventRecorder();

        await new PromoteBuildLabGenerationJob(
            coordinator.Object, telemetry, NullLogger<PromoteBuildLabGenerationJob>.Instance)
            .ExecuteAsync(CancellationToken.None);

        events.Measurements.Should().BeEmpty();
        coordinator.Verify(
            instance => instance.PromoteReadyCandidatesAsync(It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>Collects the (phase, result) tag pairs published on the Build Lab generation counter.</summary>
    private sealed class GenerationEventRecorder : IDisposable
    {
        private readonly MeterListener listener = new();
        private readonly List<(string Phase, string Result)> measurements = [];

        public GenerationEventRecorder()
        {
            listener.InstrumentPublished = (instrument, active) =>
            {
                if (instrument.Meter.Name == BuildLabTelemetry.MeterName &&
                    instrument.Name == "transcendence.buildlab.generation.events")
                {
                    active.EnableMeasurementEvents(instrument);
                }
            };
            listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
            {
                var phase = Tag(tags, "phase");
                var result = Tag(tags, "result");
                lock (measurements)
                    measurements.Add((phase, result));
            });
            listener.Start();
        }

        public IReadOnlyList<(string Phase, string Result)> Measurements
        {
            get
            {
                lock (measurements)
                    return [.. measurements];
            }
        }

        public void Dispose() => listener.Dispose();

        private static string Tag(ReadOnlySpan<KeyValuePair<string, object?>> tags, string key)
        {
            foreach (var tag in tags)
            {
                if (tag.Key == key)
                    return tag.Value?.ToString() ?? string.Empty;
            }

            return string.Empty;
        }
    }
}
