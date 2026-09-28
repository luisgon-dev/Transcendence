using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;

namespace Transcendence.Service.Core.Services.Cache;

/// <summary>
/// A <see cref="HybridCache"/> whose cache fills always run to completion, whatever the caller's
/// cancellation token says.
///
/// Every service here fills its entries with queries on the caller's scoped DbContext. HybridCache
/// returns to a cancelled caller at once while the fill is still running on that context, so a client
/// that gave up (the web proxy stops waiting at 10s) left the request disposing its context -- or
/// handing the connection back to the pool -- under a live command. On prod, under disk pressure, that
/// surfaced as ObjectDisposedException, "Index was out of range" and OutOfMemoryException from a
/// desynchronized Npgsql connection, often in an unrelated request that drew the connection next.
/// Letting the fill finish means the context is never shared or disposed mid-query, and the work is
/// not wasted: the result is cached, so the retry is instant. Removals keep their tokens.
/// </summary>
public sealed class FillToCompletionHybridCache(HybridCache inner) : HybridCache
{
    public override ValueTask<T> GetOrCreateAsync<TState, T>(
        string key,
        TState state,
        Func<TState, CancellationToken, ValueTask<T>> factory,
        HybridCacheEntryOptions? options = null,
        IEnumerable<string>? tags = null,
        CancellationToken cancellationToken = default) =>
        inner.GetOrCreateAsync(key, state, factory, options, tags, CancellationToken.None);

    public override ValueTask SetAsync<T>(
        string key,
        T value,
        HybridCacheEntryOptions? options = null,
        IEnumerable<string>? tags = null,
        CancellationToken cancellationToken = default) =>
        inner.SetAsync(key, value, options, tags, cancellationToken);

    public override ValueTask RemoveAsync(string key, CancellationToken cancellationToken = default) =>
        inner.RemoveAsync(key, cancellationToken);

    public override ValueTask RemoveAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default) =>
        inner.RemoveAsync(keys, cancellationToken);

    public override ValueTask RemoveByTagAsync(string tag, CancellationToken cancellationToken = default) =>
        inner.RemoveByTagAsync(tag, cancellationToken);

    public override ValueTask RemoveByTagAsync(IEnumerable<string> tags, CancellationToken cancellationToken = default) =>
        inner.RemoveByTagAsync(tags, cancellationToken);
}

public static class FillToCompletionHybridCacheRegistration
{
    /// <summary>
    /// Wraps the registered <see cref="HybridCache"/> (call after <c>AddHybridCache</c>) so every
    /// cache fill in the process runs to completion; see <see cref="FillToCompletionHybridCache"/>.
    /// </summary>
    public static IServiceCollection FillCacheEntriesToCompletion(this IServiceCollection services)
    {
        var registered = services.LastOrDefault(descriptor => descriptor.ServiceType == typeof(HybridCache))
            ?? throw new InvalidOperationException("Register HybridCache (AddHybridCache) before wrapping it.");
        services.Remove(registered);
        services.AddSingleton<HybridCache>(provider => new FillToCompletionHybridCache(registered switch
        {
            { ImplementationInstance: HybridCache instance } => instance,
            { ImplementationFactory: { } create } => (HybridCache)create(provider),
            { ImplementationType: { } type } => (HybridCache)ActivatorUtilities.CreateInstance(provider, type),
            _ => throw new InvalidOperationException("Unsupported HybridCache registration.")
        }));
        return services;
    }
}
