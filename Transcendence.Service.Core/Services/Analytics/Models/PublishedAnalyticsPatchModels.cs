using System.Text.Json.Serialization;

namespace Transcendence.Service.Core.Services.Analytics.Models;

[JsonConverter(typeof(JsonStringEnumConverter<AnalyticsPatchSelectionState>))]
public enum AnalyticsPatchSelectionState
{
    Ready,
    Preparing,
    Unavailable
}

public sealed record AnalyticsPatchSelection(
    AnalyticsPatchSelectionState State,
    string? ActivePatch,
    string? RequestedPatch,
    string? EffectivePatch);

public sealed record PublishedAnalyticsPatchOptionDto(
    string Patch,
    DateTime ReleasedAtUtc,
    DateTime? DetectedAtUtc,
    bool IsActive,
    int MatchCount,
    string QueueFamily,
    AnalyticsPatchSelectionState State);

public sealed record AnalyticsPublication(
    AnalyticsPatchSelection Selection,
    DateTime? EffectivePatchReleasedAtUtc = null,
    DateTime? ComputedAtUtc = null);
