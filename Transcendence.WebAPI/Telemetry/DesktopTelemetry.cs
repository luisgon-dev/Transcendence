using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.RegularExpressions;

namespace Transcendence.WebAPI.Telemetry;

/// <summary>Untrusted, opt-in aggregate client reports. Never evidence of distinct users or availability.</summary>
public sealed partial class DesktopTelemetry : IDisposable
{
    public const string MeterName = "Transcendence.Desktop";
    private readonly Meter meter = new(MeterName);
    private readonly Counter<long> events;
    private readonly Histogram<double> duration;
    private readonly HashSet<string> releases;
    private readonly object budgetLock = new();
    private readonly TimeProvider time;
    private DateTimeOffset windowStart;
    private int requests;
    private static readonly HashSet<string> Names = new(StringComparer.Ordinal)
    {
        "app_launch", "update_check", "update_download", "update_install", "update_launch",
        "backend_request", "recommendation", "league_connect", "league_import", "league_restore"
    };
    private static readonly HashSet<string> Outcomes = new(StringComparer.Ordinal)
        { "success", "failure", "timeout", "cancelled" };

    public DesktopTelemetry(IConfiguration configuration) : this(configuration, TimeProvider.System) { }
    public DesktopTelemetry(IConfiguration configuration, TimeProvider timeProvider)
    {
        time = timeProvider;
        windowStart = time.GetUtcNow();
        releases = (configuration.GetSection("DesktopTelemetry:Releases").Get<string[]>() ?? [])
            .Where(r => r.Length <= 32 && ReleasePattern().IsMatch(r)).Distinct(StringComparer.Ordinal)
            .Take(16).ToHashSet(StringComparer.Ordinal);
        events = meter.CreateCounter<long>("transcendence.desktop.events", description: "Unverified opt-in desktop event counts, not installations");
        duration = meter.CreateHistogram<double>("transcendence.desktop.duration", unit: "s", description: "Individual desktop operation duration samples");
    }

    public static bool IsValid(DesktopTelemetryBatch? batch) => batch is not null
        && batch.Release is { Length: > 0 and <= 32 } && ReleasePattern().IsMatch(batch.Release)
        && batch.Os is "windows" or "macos" or "linux"
        && batch.Arch is "x86_64" or "aarch64"
        && batch.Events is { Length: > 0 and <= 32 }
        && batch.Events.All(e => e is not null && Names.Contains(e.Name) && Outcomes.Contains(e.Outcome)
            && e.Count is >= 1 and <= 100
            && (e.DurationMs is null || (double.IsFinite(e.DurationMs.Value)
                && e.DurationMs is >= 0 and <= 600000 && e.Count == 1)));

    // Constant-memory process budget. Together with the IP limiter this bounds work even with many sources.
    public bool TryConsumeBudget()
    {
        lock (budgetLock)
        {
            var now = time.GetUtcNow();
            if (now - windowStart >= TimeSpan.FromMinutes(1)) { windowStart = now; requests = 0; }
            if (requests >= 300) return false;
            requests++;
            return true;
        }
    }

    public void Record(DesktopTelemetryBatch batch)
    {
        if (!IsValid(batch)) throw new ArgumentException("Invalid telemetry batch.", nameof(batch));
        var release = releases.Contains(batch.Release) ? batch.Release : "other";
        foreach (var e in batch.Events)
        {
            var tags = new TagList { { "release", release }, { "os", batch.Os }, { "arch", batch.Arch },
                { "event", e.Name }, { "outcome", e.Outcome } };
            events.Add(e.Count, tags);
            if (e.DurationMs is { } ms) duration.Record(ms / 1000d, tags);
        }
    }

    [GeneratedRegex(@"^[0-9]{1,5}\.[0-9]{1,5}\.[0-9]{1,5}(?:-[A-Za-z0-9.-]{1,12})?$", RegexOptions.CultureInvariant)]
    private static partial Regex ReleasePattern();
    public void Dispose() => meter.Dispose();
}
