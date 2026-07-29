using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Transcendence.Service.Core.Services.Diagnostics;

/// <summary>
/// Low-cardinality worker telemetry for the Build Lab generation pipeline. Every gauge reports 0
/// while Build Lab is disabled or while no generation occupies the state it measures, which is the
/// contract the provisioned alert rules rely on (each guards on <c>&gt; 0</c> with noDataState: OK,
/// so a disabled feature never pages). Because both Build Lab jobs ship disabled, the worker host
/// resolves this singleton at startup: an absent series and a zero series look identical on a
/// dashboard, but only the zero one proves the worker is alive with the feature off. Construction is
/// therefore the whole contract — every instrument, including the lifecycle counter's tag set, exists
/// and reads 0 from the moment the host starts.
/// </summary>
public sealed class BuildLabTelemetry : IDisposable
{
    public const string MeterName = "Transcendence.BuildLab";

    // Every (phase, result) pair the pipeline can emit. A counter series does not exist until something
    // increments it, so without seeding these the lifecycle panels and the training-failure rule would
    // read *empty* on a feature-off worker — the exact ambiguity this class exists to remove.
    private static readonly (string Phase, string Result)[] SeededEventTags =
    [
        ("create", "success"),
        ("create", "skipped"),
        ("create", "error"),
        ("training", "error"),
        ("training", "abandoned"),
        ("promote", "success"),
        ("promote", "rejected"),
        ("promote", "error"),
        ("rollback", "success")
    ];

    private readonly Meter meter = new(MeterName, "1.0.0");
    private readonly Counter<long> generationEvents;
    // Anchors, not ages: the gauges subtract these from the observation time so a reported age keeps
    // growing between the job ticks that refresh the snapshot instead of freezing at its last value.
    private long activePromotedUnixSeconds;
    private long activeSourceCutoffUnixSeconds;
    private long pendingDatasetUnixSeconds;
    private long modelingUnixSeconds;
    private long candidateUnixSeconds;
    private long publishableActionEstimates;
    private long publishablePathEstimates;
    private long championRoleScopes;
    private long matchupScopes;
    private long publishableGrade;
    private long insufficientGrade;
    private long globalFallbackGrade;
    private double overallEce;
    private double maxTimeBandEce;
    private double minimumEffectiveSampleSize;
    private double meanEffectiveSampleSize;
    private double meanAbsoluteDrift;
    private double maximumAbsoluteDrift;

    public BuildLabTelemetry()
    {
        // Instrument names are the dotted forms documented in config/monitoring/README.md; the
        // Prometheus exporter renders them as transcendence_buildlab_*_total / *_seconds, which is
        // what the provisioned dashboard and the trn-buildlab-* rules query.
        generationEvents = meter.CreateCounter<long>(
            "transcendence.buildlab.generation.events",
            unit: "{event}",
            description: "Build Lab create, training, promote, and rollback outcomes.");
        meter.CreateObservableGauge(
            "transcendence.buildlab.active_generation_age",
            () => AgeSeconds(Interlocked.Read(ref activePromotedUnixSeconds)),
            unit: "s",
            description: "Seconds since the active generation was promoted.");
        meter.CreateObservableGauge(
            "transcendence.buildlab.dataset_lag",
            () => AgeSeconds(Interlocked.Read(ref activeSourceCutoffUnixSeconds)),
            unit: "s",
            description: "Seconds between the active generation's frozen source cutoff and now.");
        meter.CreateObservableGauge(
            "transcendence.buildlab.generation.status_age",
            ObserveStatusAges,
            unit: "s",
            description: "Seconds since the oldest generation in each in-flight status last transitioned.");
        meter.CreateObservableGauge(
            "transcendence.buildlab.published_estimates",
            ObservePublishedEstimates,
            unit: "{estimate}",
            description: "Publishable estimate rows in the active generation.");
        meter.CreateObservableGauge(
            "transcendence.buildlab.coverage_scopes",
            ObserveCoverageScopes,
            unit: "{scope}",
            description: "Distinct scopes the active generation publishes at least one estimate for.");
        meter.CreateObservableGauge(
            "transcendence.buildlab.calibration_error",
            ObserveCalibrationError,
            unit: "{ece}",
            description: "Expected calibration error the active generation's validation metrics report.");
        meter.CreateObservableGauge(
            "transcendence.buildlab.effective_sample_size",
            ObserveEffectiveSampleSize,
            unit: "{observation}",
            description: "Effective sample size behind the active generation's publishable action estimates.");
        meter.CreateObservableGauge(
            "transcendence.buildlab.estimate_grades",
            ObserveEstimateGrades,
            unit: "{estimate}",
            description: "Action estimates in the active generation by evidence grade.");
        meter.CreateObservableGauge(
            "transcendence.buildlab.estimate_drift",
            ObserveEstimateDrift,
            unit: "{wpa}",
            description: "Absolute Adjusted WPA movement the most recent promotion introduced.");
        foreach (var (phase, result) in SeededEventTags)
            generationEvents.Add(0, new TagList { { "phase", phase }, { "result", result } });
    }

    /// <summary>
    /// Snapshots the active generation. A null generation is reported as zeros, which reads as
    /// "nothing promoted yet" rather than as a stale age.
    /// </summary>
    public void RecordActiveGeneration(
        DateTime? promotedAtUtc,
        DateTime? sourceCutoffUtc,
        long publishableActions,
        long publishablePaths)
    {
        Interlocked.Exchange(ref activePromotedUnixSeconds, ToUnixSeconds(promotedAtUtc));
        Interlocked.Exchange(ref activeSourceCutoffUnixSeconds, ToUnixSeconds(sourceCutoffUtc));
        Interlocked.Exchange(ref publishableActionEstimates, Math.Max(0, publishableActions));
        Interlocked.Exchange(ref publishablePathEstimates, Math.Max(0, publishablePaths));
    }

    /// <summary>
    /// Zeroes every active-generation gauge at once. With nothing promoted, each derived measurement is
    /// undefined rather than stale, and a partial reset would leave the previous generation's coverage,
    /// calibration, evidence, and drift numbers being reported against no generation at all.
    /// </summary>
    public void RecordNoActiveGeneration()
    {
        RecordActiveGeneration(null, null, 0, 0);
        RecordPublishedCoverage(0, 0);
        RecordCalibration(null, null);
        RecordEffectiveSampleSize(0, 0);
        RecordEstimateGrades(0, 0, 0);
        RecordEstimateDrift(0, 0);
    }

    /// <summary>
    /// Snapshots the oldest generation in each state the pipeline can wedge in. The Modeling anchor
    /// is the lease/heartbeat clock, so the reported age answers "how long since the modeler last
    /// showed a sign of life", which is what the wedge alert asks.
    /// </summary>
    public void RecordInFlightStatusAges(
        DateTime? oldestPendingDatasetAtUtc,
        DateTime? oldestModelingLeaseAtUtc,
        DateTime? oldestCandidateAtUtc)
    {
        Interlocked.Exchange(ref pendingDatasetUnixSeconds, ToUnixSeconds(oldestPendingDatasetAtUtc));
        Interlocked.Exchange(ref modelingUnixSeconds, ToUnixSeconds(oldestModelingLeaseAtUtc));
        Interlocked.Exchange(ref candidateUnixSeconds, ToUnixSeconds(oldestCandidateAtUtc));
    }

    /// <summary>
    /// Snapshots how much of the decision space the active generation actually publishes. A promoted
    /// generation with zero scopes serves nothing, which the published-estimate count alone can hide
    /// when a handful of rows survive the gates for one champion.
    /// </summary>
    public void RecordPublishedCoverage(long championRoleScopes, long matchupScopes)
    {
        Interlocked.Exchange(ref this.championRoleScopes, Math.Max(0, championRoleScopes));
        Interlocked.Exchange(ref this.matchupScopes, Math.Max(0, matchupScopes));
    }

    /// <summary>
    /// Snapshots the active generation's reported calibration error. An unreported or unparseable
    /// metric is 0, which means "not measured" rather than "perfectly calibrated" — the calibration
    /// rule guards on <c>&gt; 0</c> for exactly that reason.
    /// </summary>
    public void RecordCalibration(double? overallEce, double? maxTimeBandEce)
    {
        Interlocked.Exchange(ref this.overallEce, Clamp(overallEce));
        Interlocked.Exchange(ref this.maxTimeBandEce, Clamp(maxTimeBandEce));
    }

    /// <summary>
    /// Snapshots the evidence behind the published rows. The minimum is the weakest cell that still
    /// cleared the gates, so it tracks the gate boundary; the mean tracks the corpus as a whole.
    /// </summary>
    public void RecordEffectiveSampleSize(double minimum, double mean)
    {
        Interlocked.Exchange(ref minimumEffectiveSampleSize, Clamp(minimum));
        Interlocked.Exchange(ref meanEffectiveSampleSize, Clamp(mean));
    }

    /// <summary>
    /// Snapshots the grading mix of the active generation's action estimates. GLOBAL_FALLBACK is the
    /// fallback-frequency signal: a regional cell that collapsed onto the pooled global baseline.
    /// </summary>
    public void RecordEstimateGrades(long publishable, long insufficient, long globalFallback)
    {
        Interlocked.Exchange(ref publishableGrade, Math.Max(0, publishable));
        Interlocked.Exchange(ref insufficientGrade, Math.Max(0, insufficient));
        Interlocked.Exchange(ref globalFallbackGrade, Math.Max(0, globalFallback));
    }

    /// <summary>
    /// Snapshots how far the most recent promotion moved the served numbers against the generation it
    /// replaced, over the keys both published. Stays at the last promotion's value until the next one,
    /// and reads 0 when nothing has been superseded yet.
    /// </summary>
    public void RecordEstimateDrift(double meanAbsoluteDelta, double maximumAbsoluteDelta)
    {
        Interlocked.Exchange(ref meanAbsoluteDrift, Clamp(meanAbsoluteDelta));
        Interlocked.Exchange(ref maximumAbsoluteDrift, Clamp(maximumAbsoluteDelta));
    }

    public void RecordGenerationCreated() =>
        generationEvents.Add(1, new TagList { { "phase", "create" }, { "result", "success" } });

    /// <summary>A create tick that had nothing to do: proves the daily job is alive and running.</summary>
    public void RecordGenerationSkipped() =>
        generationEvents.Add(1, new TagList { { "phase", "create" }, { "result", "skipped" } });

    public void RecordGenerationCreationFailed() =>
        generationEvents.Add(1, new TagList { { "phase", "create" }, { "result", "error" } });

    /// <summary>A modeling lease expired without a heartbeat, so the training run is lost.</summary>
    public void RecordTrainingFailed() =>
        generationEvents.Add(1, new TagList { { "phase", "training" }, { "result", "error" } });

    /// <summary>An operator abandoned an in-flight generation: deliberate, so not a training fault.</summary>
    public void RecordTrainingAbandoned() =>
        generationEvents.Add(1, new TagList { { "phase", "training" }, { "result", "abandoned" } });

    public void RecordPromotionSucceeded() =>
        generationEvents.Add(1, new TagList { { "phase", "promote" }, { "result", "success" } });

    /// <summary>A candidate failed an evidence, manifest, or validation gate — normal and self-healing.</summary>
    public void RecordPromotionRejected() =>
        generationEvents.Add(1, new TagList { { "phase", "promote" }, { "result", "rejected" } });

    public void RecordPromotionFailed() =>
        generationEvents.Add(1, new TagList { { "phase", "promote" }, { "result", "error" } });

    public void RecordRollback() =>
        generationEvents.Add(1, new TagList { { "phase", "rollback" }, { "result", "success" } });

    public void Dispose() => meter.Dispose();

    // Statuses are emitted on every observation, including the empty ones, so a status that drains
    // reports 0 instead of dropping its series and leaving the last non-zero sample to go stale.
    private IEnumerable<Measurement<long>> ObserveStatusAges() =>
    [
        new(AgeSeconds(Interlocked.Read(ref pendingDatasetUnixSeconds)),
            new KeyValuePair<string, object?>("status", "PendingDataset")),
        new(AgeSeconds(Interlocked.Read(ref modelingUnixSeconds)),
            new KeyValuePair<string, object?>("status", "Modeling")),
        new(AgeSeconds(Interlocked.Read(ref candidateUnixSeconds)),
            new KeyValuePair<string, object?>("status", "Candidate"))
    ];

    private IEnumerable<Measurement<long>> ObservePublishedEstimates() =>
    [
        new(Interlocked.Read(ref publishableActionEstimates),
            new KeyValuePair<string, object?>("kind", "action")),
        new(Interlocked.Read(ref publishablePathEstimates),
            new KeyValuePair<string, object?>("kind", "path"))
    ];

    private IEnumerable<Measurement<long>> ObserveCoverageScopes() =>
    [
        new(Interlocked.Read(ref championRoleScopes),
            new KeyValuePair<string, object?>("scope", "champion_role")),
        new(Interlocked.Read(ref matchupScopes),
            new KeyValuePair<string, object?>("scope", "matchup"))
    ];

    private IEnumerable<Measurement<double>> ObserveCalibrationError() =>
    [
        new(Volatile.Read(ref overallEce), new KeyValuePair<string, object?>("metric", "overall_ece")),
        new(Volatile.Read(ref maxTimeBandEce),
            new KeyValuePair<string, object?>("metric", "max_time_band_ece"))
    ];

    private IEnumerable<Measurement<double>> ObserveEffectiveSampleSize() =>
    [
        new(Volatile.Read(ref minimumEffectiveSampleSize),
            new KeyValuePair<string, object?>("stat", "minimum")),
        new(Volatile.Read(ref meanEffectiveSampleSize), new KeyValuePair<string, object?>("stat", "mean"))
    ];

    // The three grades GradeActionEstimatesAsync can assign, always emitted together so a share can be
    // computed from the sum without one grade's series disappearing when it drains to zero.
    private IEnumerable<Measurement<long>> ObserveEstimateGrades() =>
    [
        new(Interlocked.Read(ref publishableGrade),
            new KeyValuePair<string, object?>("quality", "PUBLISHABLE")),
        new(Interlocked.Read(ref insufficientGrade),
            new KeyValuePair<string, object?>("quality", "INSUFFICIENT")),
        new(Interlocked.Read(ref globalFallbackGrade),
            new KeyValuePair<string, object?>("quality", "GLOBAL_FALLBACK"))
    ];

    private IEnumerable<Measurement<double>> ObserveEstimateDrift() =>
    [
        new(Volatile.Read(ref meanAbsoluteDrift), new KeyValuePair<string, object?>("stat", "mean_abs")),
        new(Volatile.Read(ref maximumAbsoluteDrift), new KeyValuePair<string, object?>("stat", "max_abs"))
    ];

    // A NaN or infinite aggregate would poison the series for the whole scrape lifetime, and a
    // negative one cannot happen for a count, an error, or an absolute delta.
    private static double Clamp(double? value) => value.HasValue && double.IsFinite(value.Value)
        ? Math.Max(0, value.Value)
        : 0;

    private static long AgeSeconds(long anchorUnixSeconds) => anchorUnixSeconds <= 0
        ? 0
        : Math.Max(0, DateTimeOffset.UtcNow.ToUnixTimeSeconds() - anchorUnixSeconds);

    private static long ToUnixSeconds(DateTime? value) => value.HasValue
        ? new DateTimeOffset(DateTime.SpecifyKind(value.Value, DateTimeKind.Utc)).ToUnixTimeSeconds()
        : 0;
}
