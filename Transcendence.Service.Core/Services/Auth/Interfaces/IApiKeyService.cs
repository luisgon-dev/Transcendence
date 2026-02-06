using Transcendence.Service.Core.Services.Auth.Models;

namespace Transcendence.Service.Core.Services.Auth.Interfaces;

public interface IApiKeyService
{
    Task<ApiKeyCreateResult> CreateAsync(ApiKeyCreateRequest request, CancellationToken ct = default);
    Task<ApiKeyValidationResult?> ValidateAsync(string plaintextKey, CancellationToken ct = default);
    Task<IReadOnlyList<ApiKeyListItem>> ListAsync(CancellationToken ct = default);
    Task<bool> RevokeAsync(Guid id, CancellationToken ct = default);
    Task<ApiKeyCreateResult?> RotateAsync(Guid id, CancellationToken ct = default);
}
