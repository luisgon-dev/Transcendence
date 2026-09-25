using System.Diagnostics.Metrics;

namespace Transcendence.Service.Core.Services.Diagnostics;

/// <summary>
/// Worker telemetry for the Build Lab stats refresh. Every series exists from construction and reads 0
/// while Build Lab is disabled or has never run, which is what the provisioned alert rules rely on:
/// each guards on <c>&gt; 0</c> with noDataState OK, so a disabled feature never pages, and a zero
/// series proves the worker is alive where an absent one would not.
/// </summary>
public sealed class BuildLabTelemetry : IDisposable
{
    public const string MeterName = "Transcendence.BuildLab";

    private static readonly string[] Results = ["success", "error", "skipped"];

    private readonly Meter meter = new(MeterName, "2.0.0");
    private readonly Counter<long> refreshRuns;
    private readonly Counter<long> matchesCounted;
    // An anchor, not an age: the gauge subtracts it from the observation time so the reported age keeps
    // growing between runs instead of freezing at the value the last run wrote.
    private long lastSuccessUnixSeconds;
    private long backlogMatches;

    public BuildLabTelemetry()
    {
        refreshRuns = meter.CreateCounter<long>(
            "transcendence_buildlab_refresh_runs",
            description: "Build Lab stats refresh runs by result.");
        matchesCounted = meter.CreateCounter<long>(
            "transcendence_buildlab_matches_counted",
            description: "Matches whose build decisions were added to the Build Lab stats.");
        meter.CreateObservableGauge(
            "transcendence_buildlab_last_success_age_seconds",
            () =>
            {
                var anchor = Interlocked.Read(ref lastSuccessUnixSeconds);
                return anchor == 0 ? 0 : Math.Max(1, DateTimeOffset.UtcNow.ToUnixTimeSeconds() - anchor);
            },
            unit: "s",
            description: "Seconds since the Build Lab refresh last completed; 0 before the first run.");
        meter.CreateObservableGauge(
            "transcendence_buildlab_backlog_matches",
            () => Interlocked.Read(ref backlogMatches),
            description: "Eligible matches still to be counted, as of the last refresh run.");

        foreach (var result in Results)
            refreshRuns.Add(0, new KeyValuePair<string, object?>("result", result));
        matchesCounted.Add(0);
    }

    public void RecordRun(string result) =>
        refreshRuns.Add(1, new KeyValuePair<string, object?>("result", result));

    public void RecordMatchesCounted(int count) => matchesCounted.Add(count);

    public void RecordSuccess(long backlog)
    {
        Interlocked.Exchange(ref lastSuccessUnixSeconds, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        Interlocked.Exchange(ref backlogMatches, backlog);
    }

    public void Dispose() => meter.Dispose();
}
