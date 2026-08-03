namespace Transcendence.Service.Core.Services.Analytics.Models;

public record BuildLabGenerationDto(
    Guid Id,
    string Status,
    bool IsActive,
    string Patch,
    string RankScope,
    string DatasetVersion,
    string ModelVersion,
    string CodeRevision,
    DateTime SourceCutoffUtc,
    long MatchCount,
    long ActionEstimateCount,
    long PublishableActionCount,
    string? ArtifactUri,
    string ValidationMetricsJson,
    string? FailureReason,
    string? LeaseOwner,
    string PromotionHistoryJson,
    DateTime CreatedAtUtc,
    DateTime? CompletedAtUtc,
    DateTime? PromotedAtUtc);

public record BuildLabGenerationAdminResponse(
    IReadOnlyList<BuildLabGenerationDto> Generations,
    int ActiveChampionRoleScopes,
    int ActiveMatchupScopes);

public record BuildLabFailGenerationRequest(string? Reason);

public record BuildLabPromotionHistoryEntry(string Action, DateTime AtUtc, string? Actor, string? Reason);
