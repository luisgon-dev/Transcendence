namespace Transcendence.Service.Core.Services.Auth.Models;

public record AdminAuditWriteRequest(
    Guid? ActorUserAccountId,
    string? ActorEmail,
    string Action,
    string? TargetType,
    string? TargetId,
    string? RequestId,
    bool IsSuccess,
    object? Metadata
);

public record AdminAuditEntryDto(
    Guid Id,
    Guid? ActorUserAccountId,
    string? ActorEmail,
    string Action,
    string? TargetType,
    string? TargetId,
    string? RequestId,
    bool IsSuccess,
    string? MetadataJson,
    DateTime CreatedAtUtc
);
