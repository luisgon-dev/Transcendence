using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Transcendence.WebAPI.Telemetry;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DesktopTelemetryBatch
{
    [Required, MaxLength(32)] public string Release { get; init; } = "";
    [Required] public string Os { get; init; } = "";
    [Required] public string Arch { get; init; } = "";
    [Required, MinLength(1), MaxLength(32)] public DesktopTelemetryEvent[] Events { get; init; } = [];
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DesktopTelemetryEvent
{
    [Required] public string Name { get; init; } = "";
    [Required] public string Outcome { get; init; } = "";
    [Range(1, 100)] public int Count { get; init; } = 1;
    [Range(0, 600000)] public double? DurationMs { get; init; }
}
