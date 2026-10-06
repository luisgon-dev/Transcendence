using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Transcendence.Data;
using Transcendence.Service.Core.Services.Analytics.Interfaces;
using Transcendence.Service.Core.Services.Analytics.Models;

namespace Transcendence.Service.Core.Services.Analytics.Implementations;

/// <summary>
/// Serves the dataset-stats snapshot from an in-process cache in front of a single point lookup on the
/// <c>AnalyticsResponseSnapshots</c> unique index. The entry is local-only: the snapshot changes every few
/// minutes and the lookup is cheap, so a Redis round trip would cost about as much as it saves and would
/// need cross-host invalidation. HybridCache runs one fill per key at a time, so a burst of requests on a
/// cold cache still issues one query.
/// </summary>
public sealed class DatasetStatsService(TranscendenceContext db, HybridCache cache) : IDatasetStatsService
{
    internal const string CacheKey = "dataset-stats:v1";

    private static readonly HybridCacheEntryOptions CacheOptions = new()
    {
        Expiration = TimeSpan.FromSeconds(60),
        LocalCacheExpiration = TimeSpan.FromSeconds(60),
        Flags = HybridCacheEntryFlags.DisableDistributedCache
    };

    public async Task<DatasetStatsDto?> GetAsync(CancellationToken ct = default) =>
        await cache.GetOrCreateAsync(
            CacheKey,
            async token => await ReadSnapshotAsync(token),
            CacheOptions,
            cancellationToken: ct);

    private async Task<DatasetStatsDto?> ReadSnapshotAsync(CancellationToken ct)
    {
        var payload = await db.AnalyticsResponseSnapshots
            .AsNoTracking()
            .Where(x => x.Feature == AnalyticsSnapshotSerialization.DatasetStatsFeature &&
                        x.ScopeKey == AnalyticsSnapshotSerialization.DatasetStatsScopeKey &&
                        x.Patch == AnalyticsSnapshotSerialization.PatchIndependent)
            .Select(x => x.Payload)
            .FirstOrDefaultAsync(ct);

        return payload is null ? null : AnalyticsSnapshotSerialization.Deserialize<DatasetStatsDto>(payload);
    }
}
