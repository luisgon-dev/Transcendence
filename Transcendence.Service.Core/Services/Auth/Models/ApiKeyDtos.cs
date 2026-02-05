namespace Transcendence.Service.Core.Services.Auth.Models;

public record ApiKeyCreateRequest(
    string Name,
    DateTime? ExpiresAt = null
);

public record ApiKeyCreateResult(
    Guid Id,
    string Name,
    string PlaintextKey,
    string Prefix,
    DateTime CreatedAt,
    DateTime? ExpiresAt
);

public record ApiKeyListItem(
    Guid Id,
    string Name,
    string Prefix,
    bool IsRevoked,
    DateTime CreatedAt,
    DateTime? ExpiresAt,
    DateTime? LastUsedAt
);

public record ApiKeyValidationResult(
    Guid Id,
    string Name,
    bool IsBootstrap = false
);
