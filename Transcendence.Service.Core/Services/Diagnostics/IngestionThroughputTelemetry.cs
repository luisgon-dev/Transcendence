using System.Diagnostics;
using System.Diagnostics.Metrics;
using Transcendence.Service.Core.Services.Jobs.Priority;

namespace Transcendence.Service.Core.Services.Diagnostics;

public interface IIngestionThroughputTelemetry
{
    void RecordBudgetDecision(
        string producer,
        AdaptiveThroughputBudgetDecision decision,
        bool isApiPriorityDemandActive,
        string source);

    void RecordGuardrailDecision(
        string producer,
        StarvationGuardrailDecision decision,
        string source);

    void RecordCatchUpWindowLifecycle(
        string producer,
        string outcome,
        TimeSpan catchUpWindowTtl,
        TimeSpan catchUpCooldownTtl,
        string source);

    void RecordQueueTargetOutput(
        string producer,
        int queueTarget,
        int queuedCount,
        int maxCandidates,
        AdaptiveThroughputBudgetMode mode,
        StarvationGuardrailOutcome guardrailOutcome,
        bool isForcedCatchUpActive,
        string outcome,
        string source);
}

public sealed class IngestionThroughputTelemetry : IIngestionThroughputTelemetry, IDisposable
{
    private const string MeterName = "Transcendence.IngestionThroughput";
    private const string MeterVersion = "1.0.0";
    private const string UnknownValue = "unknown";
    private const string DefaultQueueTier = "refresh-low";

    private readonly ILogger<IngestionThroughputTelemetry> logger;
    private readonly Meter meter;
    private readonly Counter<long> decisionEventsCounter;
    private readonly Counter<long> deferAgeBreachCounter;
    private readonly Counter<long> catchUpLifecycleCounter;
    private readonly Histogram<long> queueTargetHistogram;
    private readonly Histogram<long> queuedCountHistogram;
    private readonly Histogram<double> catchUpWindowMinutesHistogram;

    public IngestionThroughputTelemetry(ILogger<IngestionThroughputTelemetry> logger)
    {
        this.logger = logger;
        meter = new Meter(MeterName, MeterVersion);
        decisionEventsCounter = meter.CreateCounter<long>(
            "transcendence.ingestion_throughput.decisions",
            unit: "{event}",
            description: "Count of throughput budget and guardrail decisions by producer, mode, and outcome.");
        deferAgeBreachCounter = meter.CreateCounter<long>(
            "transcendence.ingestion_throughput.defer_age_breaches",
            unit: "{event}",
            description: "Count of defer-age threshold breaches detected by starvation guardrail evaluation.");
        catchUpLifecycleCounter = meter.CreateCounter<long>(
            "transcendence.ingestion_throughput.catch_up.lifecycle",
            unit: "{event}",
            description: "Count of forced catch-up window lifecycle outcomes.");
        queueTargetHistogram = meter.CreateHistogram<long>(
            "transcendence.ingestion_throughput.queue_target",
            unit: "{job}",
            description: "Queue target selected for low-priority producers.");
        queuedCountHistogram = meter.CreateHistogram<long>(
            "transcendence.ingestion_throughput.queued_count",
            unit: "{job}",
            description: "Actual queued low-priority jobs per producer run.");
        catchUpWindowMinutesHistogram = meter.CreateHistogram<double>(
            "transcendence.ingestion_throughput.catch_up.window_minutes",
            unit: "min",
            description: "Configured forced catch-up window duration in minutes.");
    }

    public void RecordBudgetDecision(
        string producer,
        AdaptiveThroughputBudgetDecision decision,
        bool isApiPriorityDemandActive,
        string source)
    {
        EmitNonBlocking("ingestion_throughput.budget_decision", () =>
        {
            var normalizedProducer = NormalizeToken(producer, UnknownValue);
            var normalizedMode = NormalizeToken(decision.Mode.ToString(), UnknownValue);
            var normalizedSource = NormalizeToken(source, UnknownValue);
            var normalizedOutcome = isApiPriorityDemandActive ? "api_priority_active" : "budget_applied";
            var tags = BuildTags(normalizedProducer, normalizedMode, normalizedOutcome, normalizedSource);

            decisionEventsCounter.Add(1, tags);
            queueTargetHistogram.Record(Math.Max(0, decision.QueueTarget), tags);

            logger.LogDebug(
                "event=ingestion_throughput.budget_decision producer={Producer} queue_tier={QueueTier} mode={Mode} outcome={Outcome} source={Source} queue_target={QueueTarget} max_candidates={MaxCandidates} include_all_modes={IncludeAllModes} coverage_ratio={CoverageRatio:F2} backlog_age_minutes={BacklogAgeMinutes:F1} velocity_per_hour={VelocityPerHour:F2} pressure_ratio={PressureRatio:F2}",
                normalizedProducer,
                DefaultQueueTier,
                normalizedMode,
                normalizedOutcome,
                normalizedSource,
                Math.Max(0, decision.QueueTarget),
                Math.Max(0, decision.MaxCandidates),
                decision.IncludeAllModes,
                decision.CoverageRatio,
                decision.BacklogAgeMinutes,
                decision.RecentVelocityPerHour,
                decision.CandidatePressureRatio);
        });
    }

    public void RecordGuardrailDecision(
        string producer,
        StarvationGuardrailDecision decision,
        string source)
    {
        EmitNonBlocking("ingestion_throughput.guardrail_decision", () =>
        {
            var normalizedProducer = NormalizeToken(producer, UnknownValue);
            var normalizedOutcome = NormalizeToken(decision.Outcome.ToString(), UnknownValue);
            var normalizedSource = NormalizeToken(source, UnknownValue);
            var tags = BuildTags(normalizedProducer, "guardrail", normalizedOutcome, normalizedSource);

            decisionEventsCounter.Add(1, tags);

            var maxDeferAgeMinutes = Math.Max(0d, decision.MaxEligibleDeferAgeMinutes);
            var deferThresholdMinutes = Math.Max(0d, decision.DeferAgeThresholdMinutes);
            var deferAgeBreached = deferThresholdMinutes > 0d && maxDeferAgeMinutes >= deferThresholdMinutes;
            if (deferAgeBreached)
            {
                var breachTags = BuildTags(normalizedProducer, "guardrail", "defer_age_breach", normalizedSource);
                deferAgeBreachCounter.Add(1, breachTags);
            }

            logger.LogDebug(
                "event=ingestion_throughput.guardrail_decision producer={Producer} queue_tier={QueueTier} mode=guardrail outcome={Outcome} source={Source} force_catch_up={ForceCatchUp} start_window={StartWindow} queue_target={QueueTarget} max_candidates={MaxCandidates} defer_age_minutes={DeferAgeMinutes:F1} defer_threshold_minutes={DeferThresholdMinutes:F1}",
                normalizedProducer,
                DefaultQueueTier,
                normalizedOutcome,
                normalizedSource,
                decision.IsForcedCatchUpActive,
                decision.ShouldStartCatchUpWindow,
                Math.Max(0, decision.QueueTarget),
                Math.Max(0, decision.MaxCandidates),
                maxDeferAgeMinutes,
                deferThresholdMinutes);
        });
    }

    public void RecordCatchUpWindowLifecycle(
        string producer,
        string outcome,
        TimeSpan catchUpWindowTtl,
        TimeSpan catchUpCooldownTtl,
        string source)
    {
        EmitNonBlocking("ingestion_throughput.catch_up_window", () =>
        {
            var normalizedProducer = NormalizeToken(producer, UnknownValue);
            var normalizedOutcome = NormalizeToken(outcome, UnknownValue);
            var normalizedSource = NormalizeToken(source, UnknownValue);
            var normalizedWindowMinutes = Math.Max(0d, catchUpWindowTtl.TotalMinutes);
            var normalizedCooldownMinutes = Math.Max(0d, catchUpCooldownTtl.TotalMinutes);
            var tags = BuildTags(normalizedProducer, "catch_up", normalizedOutcome, normalizedSource);

            catchUpLifecycleCounter.Add(1, tags);
            catchUpWindowMinutesHistogram.Record(normalizedWindowMinutes, tags);

            logger.LogDebug(
                "event=ingestion_throughput.catch_up_window producer={Producer} queue_tier={QueueTier} mode=catch_up outcome={Outcome} source={Source} catch_up_window_minutes={CatchUpWindowMinutes:F1} catch_up_cooldown_minutes={CatchUpCooldownMinutes:F1}",
                normalizedProducer,
                DefaultQueueTier,
                normalizedOutcome,
                normalizedSource,
                normalizedWindowMinutes,
                normalizedCooldownMinutes);
        });
    }

    public void RecordQueueTargetOutput(
        string producer,
        int queueTarget,
        int queuedCount,
        int maxCandidates,
        AdaptiveThroughputBudgetMode mode,
        StarvationGuardrailOutcome guardrailOutcome,
        bool isForcedCatchUpActive,
        string outcome,
        string source)
    {
        EmitNonBlocking("ingestion_throughput.queue_output", () =>
        {
            var normalizedProducer = NormalizeToken(producer, UnknownValue);
            var normalizedMode = NormalizeToken(mode.ToString(), UnknownValue);
            var normalizedOutcome = NormalizeToken(outcome, UnknownValue);
            var normalizedSource = NormalizeToken(source, UnknownValue);
            var normalizedGuardrail = NormalizeToken(guardrailOutcome.ToString(), UnknownValue);
            var normalizedQueueTarget = Math.Max(0, queueTarget);
            var normalizedQueuedCount = Math.Max(0, queuedCount);
            var normalizedMaxCandidates = Math.Max(0, maxCandidates);
            var tags = BuildTags(normalizedProducer, normalizedMode, normalizedOutcome, normalizedSource);

            queueTargetHistogram.Record(normalizedQueueTarget, tags);
            queuedCountHistogram.Record(normalizedQueuedCount, tags);

            logger.LogDebug(
                "event=ingestion_throughput.queue_output producer={Producer} queue_tier={QueueTier} mode={Mode} outcome={Outcome} source={Source} guardrail_outcome={GuardrailOutcome} force_catch_up={ForceCatchUp} queue_target={QueueTarget} queued={QueuedCount} max_candidates={MaxCandidates}",
                normalizedProducer,
                DefaultQueueTier,
                normalizedMode,
                normalizedOutcome,
                normalizedSource,
                normalizedGuardrail,
                isForcedCatchUpActive,
                normalizedQueueTarget,
                normalizedQueuedCount,
                normalizedMaxCandidates);
        });
    }

    public void Dispose()
    {
        meter.Dispose();
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
                "event=ingestion_throughput.telemetry_failure telemetry_event={TelemetryEvent} outcome=ignored",
                eventName);
        }
    }

    private static string NormalizeToken(string value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;

        return value.Trim().ToLowerInvariant();
    }

    private static TagList BuildTags(string producer, string mode, string outcome, string source)
    {
        return new TagList
        {
            { "producer", producer },
            { "queue_tier", DefaultQueueTier },
            { "mode", mode },
            { "outcome", outcome },
            { "source", source }
        };
    }
}
