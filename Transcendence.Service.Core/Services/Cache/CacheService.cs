using Microsoft.Extensions.Caching.Hybrid;

namespace Transcendence.Service.Core.Services.Cache;

public class CacheService(HybridCache cache) : ICacheService
{
    public async Task<T> GetOrCreateAsync<T>(
        string key,
        Func<CancellationToken, Task<T>> factory,
        TimeSpan? expiration = null,
        TimeSpan? localExpiration = null,
        string[]? tags = null,
        CancellationToken cancellationToken = default)
    {
        var options = new HybridCacheEntryOptions
        {
            Expiration = expiration,
            LocalCacheExpiration = localExpiration
        };

        return await cache.GetOrCreateAsync(
            key,
            async (ct) => await factory(ct),
            options,
            tags,
            cancellationToken);
    }

    public async Task RemoveByTagAsync(string tag, CancellationToken cancellationToken = default)
    {
        await cache.RemoveByTagAsync(tag, cancellationToken);
    }
}
