namespace Transcendence.Service.Core.Services.Analytics.Models;

public record AnalyticsPatchStatusDto(
    string? Patch,
    DateTime? ActivePatchReleasedAtUtc,
    DateTime? ActivePatchDetectedAtUtc
);
