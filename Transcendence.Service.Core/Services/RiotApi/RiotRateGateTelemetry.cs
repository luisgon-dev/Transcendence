using System.Diagnostics.Metrics;

namespace Transcendence.Service.Core.Services.RiotApi;

/// <summary>
/// Metrics for the <see cref="RiotRateGate"/> — the central per-region pacing constraint against the
/// low-rate Riot key. A "rejected" acquisition means a region's budget stayed exhausted past the gate's
/// max wait, which is the exact saturation signal that preceded the historical ingestion stall.
/// Registered as a singleton in the worker; the gate records to it when enabled.
/// </summary>
public sealed class RiotRateGateTelemetry : IDisposable
{
    public const string MeterName = "Transcendence.RiotRateGate";

    private readonly Meter _meter;
    private readonly Counter<long> _acquisitions;
    private readonly Histogram<double> _waitSeconds;

    public RiotRateGateTelemetry()
    {
        _meter = new Meter(MeterName);
        _acquisitions = _meter.CreateCounter<long>(
            "transcendence.riot_rate_gate.acquisitions",
            unit: "{acquisition}",
            description: "Rate-gate token acquisition attempts, tagged by region and outcome (acquired|rejected).");
        _waitSeconds = _meter.CreateHistogram<double>(
            "transcendence.riot_rate_gate.wait",
            unit: "s",
            description: "Time spent waiting for a rate-gate token, tagged by region.");
    }

    public void RecordAcquire(string region, bool acquired, double waitSeconds)
    {
        _acquisitions.Add(
            1,
            new KeyValuePair<string, object?>("region", region),
            new KeyValuePair<string, object?>("outcome", acquired ? "acquired" : "rejected"));
        _waitSeconds.Record(waitSeconds, new KeyValuePair<string, object?>("region", region));
    }

    public void Dispose() => _meter.Dispose();
}
