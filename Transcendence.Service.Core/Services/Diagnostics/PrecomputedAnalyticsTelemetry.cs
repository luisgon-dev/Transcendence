using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Transcendence.Service.Core.Services.Diagnostics;

/// <summary>
/// Low-cardinality worker telemetry for the resumable matchup pipeline.
/// </summary>
public sealed class PrecomputedAnalyticsTelemetry : IDisposable
{
    public const string MeterName = "Transcendence.AnalyticsRefresh";

    private readonly Meter meter = new(MeterName, "1.0.0");
    private readonly Counter<long> events;
    private readonly Counter<long> sourceMatches;
    private readonly Counter<long> factsWritten;
    private readonly Counter<long> aggregateRowsWritten;
    private readonly Histogram<double> batchDurationMilliseconds;
    private readonly Histogram<double> generationDurationMilliseconds;
    private long lastSuccessUnixSeconds;
    private long activeGenerationRows;
    private long resumeAttempt;

    public PrecomputedAnalyticsTelemetry()
    {
        events = meter.CreateCounter<long>(
            "transcendence.analytics.matchup.refresh_events",
            unit: "{event}",
            description: "Matchup source, batch, and generation lifecycle outcomes.");
        sourceMatches = meter.CreateCounter<long>(
            "transcendence.analytics.matchup.source_matches",
            unit: "{match}",
            description: "Matches materialized into durable lane-pair facts.");
        factsWritten = meter.CreateCounter<long>(
            "transcendence.analytics.matchup.facts_written",
            unit: "{fact}",
            description: "Durable lane-pair facts inserted or replaced.");
        aggregateRowsWritten = meter.CreateCounter<long>(
            "transcendence.analytics.matchup.aggregate_rows_written",
            unit: "{row}",
            description: "Generation-scoped matchup aggregate rows committed.");
        batchDurationMilliseconds = meter.CreateHistogram<double>(
            "transcendence.analytics.matchup.batch_duration",
            unit: "ms",
            description: "Source-materialization and champion-aggregation batch duration.");
        generationDurationMilliseconds = meter.CreateHistogram<double>(
            "transcendence.analytics.matchup.generation_duration",
            unit: "ms",
            description: "End-to-end matchup generation duration.");
        meter.CreateObservableGauge(
            "transcendence.analytics.matchup.last_success_unixtime",
            () => Interlocked.Read(ref lastSuccessUnixSeconds),
            unit: "s",
            description: "Unix timestamp of the last promoted matchup generation.");
        meter.CreateObservableGauge(
            "transcendence.analytics.matchup.active_generation_rows",
            () => Interlocked.Read(ref activeGenerationRows),
            unit: "{row}",
            description: "Rows in the most recently promoted matchup generation.");
        meter.CreateObservableGauge(
            "transcendence.analytics.matchup.resume_attempt",
            () => Interlocked.Read(ref resumeAttempt),
            unit: "{attempt}",
            description: "Attempt number of the generation currently being processed.");
    }

    public void RecordSourceBatch(int matches, int facts, double elapsedMilliseconds)
    {
        var tags = new TagList { { "phase", "source" }, { "result", "success" } };
        events.Add(1, tags);
        sourceMatches.Add(Math.Max(0, matches));
        factsWritten.Add(Math.Max(0, facts));
        batchDurationMilliseconds.Record(Math.Max(0, elapsedMilliseconds), tags);
    }

    public void RecordChampionBatch(
        int champions,
        int rows,
        double elapsedMilliseconds,
        bool succeeded,
        bool split)
    {
        var tags = new TagList
        {
            { "phase", "aggregate" },
            { "result", succeeded ? "success" : "error" },
            { "split", split ? "true" : "false" }
        };
        events.Add(1, tags);
        if (succeeded)
            aggregateRowsWritten.Add(Math.Max(0, rows));
        batchDurationMilliseconds.Record(Math.Max(0, elapsedMilliseconds), tags);
    }

    public void RecordGenerationStarted(int attempt) =>
        Interlocked.Exchange(ref resumeAttempt, Math.Max(1, attempt));

    public void RecordGenerationSucceeded(int rows, double elapsedMilliseconds)
    {
        Interlocked.Exchange(ref activeGenerationRows, Math.Max(0, rows));
        Interlocked.Exchange(ref lastSuccessUnixSeconds, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        Interlocked.Exchange(ref resumeAttempt, 0);
        events.Add(1, new TagList { { "phase", "generation" }, { "result", "success" } });
        generationDurationMilliseconds.Record(
            Math.Max(0, elapsedMilliseconds),
            new TagList { { "result", "success" } });
    }

    public void RecordGenerationFailed(int attempt, double elapsedMilliseconds)
    {
        Interlocked.Exchange(ref resumeAttempt, Math.Max(1, attempt));
        events.Add(1, new TagList { { "phase", "generation" }, { "result", "error" } });
        generationDurationMilliseconds.Record(
            Math.Max(0, elapsedMilliseconds),
            new TagList { { "result", "error" } });
    }

    public void Dispose() => meter.Dispose();
}
