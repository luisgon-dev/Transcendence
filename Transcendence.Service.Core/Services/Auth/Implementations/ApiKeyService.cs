using System.Security.Cryptography;
using System.Text;
using Transcendence.Data.Models.Auth;
using Transcendence.Data.Repositories.Interfaces;
using Transcendence.Service.Core.Services.Auth.Interfaces;
using Transcendence.Service.Core.Services.Auth.Models;

namespace Transcendence.Service.Core.Services.Auth.Implementations;

public class ApiKeyService(
    IApiClientKeyRepository apiKeyRepository,
    IConfiguration configuration) : IApiKeyService
{
    private const string BootstrapKeyConfigPath = "Auth:BootstrapApiKey";
    private static readonly TimeSpan LastUsedWriteInterval = TimeSpan.FromMinutes(10);

    public async Task<ApiKeyCreateResult> CreateAsync(ApiKeyCreateRequest request, CancellationToken ct = default)
    {
        var name = request.Name.Trim();
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("API key name is required.", nameof(request));

        var plaintextKey = GenerateKey();
        var hash = HashKey(plaintextKey);
        var now = DateTime.UtcNow;

        var entity = new ApiClientKey
        {
            Id = Guid.NewGuid(),
            Name = name,
            KeyHash = hash,
            KeyPrefix = GetPrefix(plaintextKey),
            CreatedAt = now,
            ExpiresAt = request.ExpiresAt,
            IsRevoked = false
        };

        await apiKeyRepository.AddAsync(entity, ct);
        await apiKeyRepository.SaveChangesAsync(ct);

        return new ApiKeyCreateResult(
            entity.Id,
            entity.Name,
            plaintextKey,
            entity.KeyPrefix,
            entity.CreatedAt,
            entity.ExpiresAt
        );
    }

    public async Task<ApiKeyValidationResult?> ValidateAsync(string plaintextKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(plaintextKey))
            return null;

        var bootstrap = configuration[BootstrapKeyConfigPath];
        if (!string.IsNullOrWhiteSpace(bootstrap) &&
            string.Equals(bootstrap.Trim(), plaintextKey.Trim(), StringComparison.Ordinal))
        {
            return new ApiKeyValidationResult(Guid.Empty, "Bootstrap", true);
        }

        var hash = HashKey(plaintextKey);
        var key = await apiKeyRepository.GetActiveByHashAsync(hash, ct);
        if (key == null) return null;

        var now = DateTime.UtcNow;
        if (!key.LastUsedAt.HasValue || now - key.LastUsedAt.Value >= LastUsedWriteInterval)
        {
            key.LastUsedAt = now;
            await apiKeyRepository.SaveChangesAsync(ct);
        }

        return new ApiKeyValidationResult(key.Id, key.Name);
    }

    public async Task<IReadOnlyList<ApiKeyListItem>> ListAsync(CancellationToken ct = default)
    {
        var keys = await apiKeyRepository.ListAsync(ct);
        return keys.Select(x => new ApiKeyListItem(
            x.Id,
            x.Name,
            x.KeyPrefix,
            x.IsRevoked,
            x.CreatedAt,
            x.ExpiresAt,
            x.LastUsedAt
        )).ToList();
    }

    public Task<bool> RevokeAsync(Guid id, CancellationToken ct = default)
    {
        return apiKeyRepository.RevokeAsync(id, ct);
    }

    public async Task<ApiKeyCreateResult?> RotateAsync(Guid id, CancellationToken ct = default)
    {
        var existing = await apiKeyRepository.GetByIdAsync(id, ct);
        if (existing == null) return null;

        await apiKeyRepository.RevokeAsync(id, ct);

        return await CreateAsync(
            new ApiKeyCreateRequest(existing.Name, existing.ExpiresAt),
            ct);
    }

    private static string GenerateKey()
    {
        var random = RandomNumberGenerator.GetBytes(32);
        return $"trn_{Convert.ToHexString(random).ToLowerInvariant()}";
    }

    private static string HashKey(string key)
    {
        var bytes = Encoding.UTF8.GetBytes(key);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string GetPrefix(string key)
    {
        if (key.Length <= 12) return key;
        return key[..12];
    }
}
