using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Transcendence.Service.Core.Services.Diagnostics;

public sealed class LeaderboardTelemetry : IDisposable
{
    public const string MeterName = "Transcendence.Leaderboards";

    private readonly Meter meter = new(MeterName, "1.0.0");
    private readonly Counter<long> requests;
    private readonly Histogram<double> durationMilliseconds;

    public LeaderboardTelemetry()
    {
        requests = meter.CreateCounter<long>(
            "transcendence.leaderboard.requests",
            description: "Leaderboard requests partitioned by query kind, cache outcome, and result.");
        durationMilliseconds = meter.CreateHistogram<double>(
            "transcendence.leaderboard.duration",
            unit: "ms",
            description: "End-to-end leaderboard service duration, including cache lookup.");
    }

    public void Record(string kind, bool cacheMiss, bool succeeded, double elapsedMilliseconds)
    {
        var tags = new TagList
        {
            { "leaderboard.kind", kind },
            { "cache.outcome", cacheMiss ? "miss" : "hit" },
            { "result", succeeded ? "success" : "error" }
        };
        requests.Add(1, tags);
        durationMilliseconds.Record(elapsedMilliseconds, tags);
    }

    public void Dispose() => meter.Dispose();
}
