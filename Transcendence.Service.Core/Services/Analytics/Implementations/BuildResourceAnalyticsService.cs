using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Transcendence.Data;
using Transcendence.Data.Models.LoL.Analytics;
using Transcendence.Service.Core.Services.Analytics.Interfaces;
using Transcendence.Service.Core.Services.Analytics.Models;
using Transcendence.Service.Core.Services.RiotApi;

namespace Transcendence.Service.Core.Services.Analytics.Implementations;

/// <summary>
/// Cached item and rune exploration backed only by a completed Build Atlas generation. The request
/// path never scans raw match resources. When the requested/default patch is still warming, default
/// reads use the newest completed patch and explicit patch reads return an empty result immediately.
/// </summary>
public sealed class BuildResourceAnalyticsService(
    TranscendenceContext context,
    HybridCache cache,
    IAnalyticsPatchQueryService patchQueryService) : IBuildResourceAnalyticsService
{
    private const string ItemType = "item";
    private const string RuneType = "rune";
    private const int IndexChampionLimit = 3;
    private const int DetailChampionLimit = 100;

    // Keys carry the generation's version (its id and counted-match total), which changes whenever the
    // refresher adds matches in place or promotes a rebuild, so entries never go stale and need no
    // patch tag -- the precomputed-analytics jobs clear that tag twice an hour for tables this does
    // not read.
    private static readonly HybridCacheEntryOptions CacheOptions = new()
    {
        Expiration = TimeSpan.FromHours(24),
        LocalCacheExpiration = TimeSpan.FromHours(1)
    };

    public Task<BuildResourceAnalyticsIndexResponse> GetItemsAsync(
        string? region,
        string? patch,
        CancellationToken ct = default) =>
        GetIndexAsync(ItemType, region, patch, ct);

    public Task<BuildResourceAnalyticsDetailResponse?> GetItemAsync(
        int itemId,
        string? region,
        string? patch,
        CancellationToken ct = default) =>
        GetDetailAsync(ItemType, itemId, region, patch, ct);

    public Task<BuildResourceAnalyticsIndexResponse> GetRunesAsync(
        string? region,
        string? patch,
        CancellationToken ct = default) =>
        GetIndexAsync(RuneType, region, patch, ct);

    public Task<BuildResourceAnalyticsDetailResponse?> GetRuneAsync(
        int runeId,
        string? region,
        string? patch,
        CancellationToken ct = default) =>
        GetDetailAsync(RuneType, runeId, region, patch, ct);

    private async Task<BuildResourceAnalyticsIndexResponse> GetIndexAsync(
        string resourceType,
        string? region,
        string? requestedPatch,
        CancellationToken ct)
    {
        var normalizedRegion = AnalyticsRegionCatalog.NormalizeOrDefault(region);
        var selection = await ResolveSnapshotAsync(requestedPatch, ct);
        if (selection is null)
            return EmptyIndex(resourceType, requestedPatch?.Trim() ?? string.Empty, normalizedRegion);

        var key = IndexKey(resourceType, selection.Version, normalizedRegion);
        return await cache.GetOrCreateAsync(
            key,
            cancel => ComputeIndexAsync(resourceType, normalizedRegion, selection, cancel),
            CacheOptions,
            tags: ["analytics"],
            cancellationToken: ct);
    }

    private async Task<BuildResourceAnalyticsDetailResponse?> GetDetailAsync(
        string resourceType,
        int resourceId,
        string? region,
        string? requestedPatch,
        CancellationToken ct)
    {
        if (resourceId <= 0)
            return null;

        var normalizedRegion = AnalyticsRegionCatalog.NormalizeOrDefault(region);
        var selection = await ResolveSnapshotAsync(requestedPatch, ct);
        if (selection is null)
            return null;

        var key = DetailKey(resourceType, resourceId, selection.Version, normalizedRegion);
        return await cache.GetOrCreateAsync(
            key,
            cancel => ComputeDetailAsync(resourceType, resourceId, normalizedRegion, selection, cancel),
            CacheOptions,
            tags: ["analytics"],
            cancellationToken: ct);
    }

    private async ValueTask<BuildResourceAnalyticsIndexResponse> ComputeIndexAsync(
        string resourceType,
        string region,
        SnapshotSelection selection,
        CancellationToken ct)
    {
        var metadata = await LoadMetadataAsync(resourceType, selection.Patch, ct);
        if (metadata.Count == 0)
            return new BuildResourceAnalyticsIndexResponse(resourceType, selection.Patch, region, 0, []);

        var snapshot = await LoadSnapshotDataAsync(
            selection.SnapshotId, resourceType, region, metadata.Keys.ToArray(), ct);
        return ProjectIndex(resourceType, selection.Patch, region, metadata, snapshot);
    }

    private static BuildResourceAnalyticsIndexResponse ProjectIndex(
        string resourceType,
        string patch,
        string region,
        IReadOnlyDictionary<int, ResourceMetadata> metadata,
        SnapshotData snapshot)
    {
        var entries = snapshot.Aggregates
            .GroupBy(row => row.ResourceId)
            .Select(group => BuildEntry(
                group.Key,
                metadata[group.Key],
                group.ToList(),
                snapshot.ChampionTotals,
                snapshot.TotalParticipantGames,
                IndexChampionLimit))
            .OrderByDescending(entry => entry.Games)
            .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new BuildResourceAnalyticsIndexResponse(
            resourceType,
            patch,
            region,
            snapshot.TotalParticipantGames,
            entries);
    }

    private async ValueTask<BuildResourceAnalyticsDetailResponse?> ComputeDetailAsync(
        string resourceType,
        int resourceId,
        string region,
        SnapshotSelection selection,
        CancellationToken ct)
    {
        var metadata = await LoadMetadataAsync(resourceType, selection.Patch, ct, resourceId);
        if (!metadata.TryGetValue(resourceId, out var resourceMetadata))
            return null;

        var snapshot = await LoadSnapshotDataAsync(
            selection.SnapshotId, resourceType, region, [resourceId], ct);
        return ProjectDetail(resourceType, resourceId, selection.Patch, region, resourceMetadata, snapshot);
    }

    private static BuildResourceAnalyticsDetailResponse? ProjectDetail(
        string resourceType,
        int resourceId,
        string patch,
        string region,
        ResourceMetadata resourceMetadata,
        SnapshotData snapshot)
    {
        if (snapshot.Aggregates.Count == 0)
            return null;

        var entry = BuildEntry(
            resourceId,
            resourceMetadata,
            snapshot.Aggregates,
            snapshot.ChampionTotals,
            snapshot.TotalParticipantGames,
            DetailChampionLimit);
        var championStats = BuildChampionStats(
            snapshot.Aggregates,
            snapshot.ChampionTotals,
            entry.Games,
            DetailChampionLimit);

        return new BuildResourceAnalyticsDetailResponse(
            resourceType,
            patch,
            region,
            snapshot.TotalParticipantGames,
            entry,
            championStats);
    }

    /// <summary>
    /// Writes the all-region index and every detail entry of a generation's version into the cache from
    /// rows the refresher already holds in memory, so no read path has to aggregate them.
    /// </summary>
    /// <remarks>
    /// The keys carry the generation version, which changes every hourly in-place add, so a page that
    /// was not requested within the hour was always a miss. A miss aggregates one resource's rows, which
    /// sit one per heap page across the stat table: 2,515 pages and 36s for one rune on prod
    /// (2026-10-06). Only the all-region scope is warmed; a region filter still computes on a miss.
    /// </remarks>
    public async Task WarmGenerationAsync(
        Guid snapshotId,
        string patch,
        int processedMatchCount,
        IReadOnlyCollection<BuildResourceStat> stats,
        IReadOnlyCollection<BuildResourcePopulationStat> populations,
        CancellationToken ct = default)
    {
        const string region = AnalyticsRegionCatalog.GlobalRegionCode;
        var version = new SnapshotSelection(snapshotId, patch, processedMatchCount).Version;
        var championTotals = populations
            .GroupBy(row => new ChampionRoleKey(row.ChampionId, row.Role))
            .ToDictionary(group => group.Key, group => group.Sum(row => row.Games));
        var totalParticipantGames = championTotals.Values.Sum();

        foreach (var resourceType in new[] { ItemType, RuneType })
        {
            var metadata = await LoadMetadataAsync(resourceType, patch, ct);
            var aggregates = stats
                .Where(row => row.ResourceType == resourceType && metadata.ContainsKey(row.ResourceId))
                .GroupBy(row => (row.ResourceId, row.ChampionId, row.Role))
                .Select(group => new ResourceAggregateRow
                {
                    ResourceId = group.Key.ResourceId,
                    ChampionId = group.Key.ChampionId,
                    Role = group.Key.Role,
                    Games = group.Sum(row => row.Games),
                    Wins = group.Sum(row => row.Wins)
                })
                .ToList();

            var index = metadata.Count == 0
                ? new BuildResourceAnalyticsIndexResponse(resourceType, patch, region, 0, [])
                : ProjectIndex(
                    resourceType, patch, region, metadata,
                    new SnapshotData(aggregates, championTotals, totalParticipantGames));
            await cache.SetAsync(IndexKey(resourceType, version, region), index, CacheOptions, ["analytics"], ct);

            foreach (var resource in aggregates.GroupBy(row => row.ResourceId))
            {
                var detail = ProjectDetail(
                    resourceType, resource.Key, patch, region, metadata[resource.Key],
                    new SnapshotData(resource.ToList(), championTotals, totalParticipantGames));
                await cache.SetAsync(
                    DetailKey(resourceType, resource.Key, version, region), detail, CacheOptions, ["analytics"], ct);
            }
        }
    }

    private static string IndexKey(string resourceType, string version, string region) =>
        $"analytics:build-resources:v3:{resourceType}:{version}:{region}";

    private static string DetailKey(string resourceType, int resourceId, string version, string region) =>
        $"analytics:build-resource:v3:{resourceType}:{resourceId}:{version}:{region}";

    private async Task<SnapshotData> LoadSnapshotDataAsync(
        Guid snapshotId,
        string resourceType,
        string region,
        int[] allowedIds,
        CancellationToken ct)
    {
        var regionFilter = AnalyticsRegionCatalog.NormalizeToFilter(region);
        var stats = context.BuildResourceStats.AsNoTracking()
            .Where(row => row.SnapshotId == snapshotId &&
                          row.ResourceType == resourceType &&
                          allowedIds.Contains(row.ResourceId));
        var population = context.BuildResourcePopulationStats.AsNoTracking()
            .Where(row => row.SnapshotId == snapshotId);
        if (regionFilter != null)
        {
            stats = stats.Where(row => row.PlatformRegion == regionFilter);
            population = population.Where(row => row.PlatformRegion == regionFilter);
        }

        var aggregates = await stats
            .GroupBy(row => new { row.ResourceId, row.ChampionId, row.Role })
            .Select(group => new ResourceAggregateRow
            {
                ResourceId = group.Key.ResourceId,
                ChampionId = group.Key.ChampionId,
                Role = group.Key.Role,
                Games = group.Sum(row => row.Games),
                Wins = group.Sum(row => row.Wins)
            })
            .ToListAsync(ct);
        var championRows = await population
            .GroupBy(row => new { row.ChampionId, row.Role })
            .Select(group => new ChampionTotalRow
            {
                ChampionId = group.Key.ChampionId,
                Role = group.Key.Role,
                Games = group.Sum(row => row.Games)
            })
            .ToListAsync(ct);
        var championTotals = championRows.ToDictionary(
            row => new ChampionRoleKey(row.ChampionId, row.Role),
            row => row.Games);
        return new SnapshotData(aggregates, championTotals, championRows.Sum(row => row.Games));
    }

    private async Task<Dictionary<int, ResourceMetadata>> LoadMetadataAsync(
        string resourceType,
        string patch,
        CancellationToken ct,
        int? resourceId = null)
    {
        if (resourceType == ItemType)
        {
            var rows = await context.ItemVersions
                .AsNoTracking()
                .Where(item => item.PatchVersion == patch && (resourceId == null || item.ItemId == resourceId))
                .Select(item => new
                {
                    item.ItemId,
                    item.Name,
                    item.Description,
                    item.BuildsFrom,
                    item.BuildsInto,
                    item.Tags,
                    item.InStore,
                    item.PriceTotal
                })
                .ToListAsync(ct);

            return rows
                .Where(item => BuildItemClassifier.IsCompletedBuildItem(new BuildItemMetadata(
                    item.BuildsFrom,
                    item.BuildsInto,
                    item.Tags,
                    item.InStore,
                    item.PriceTotal)) ||
                    BuildItemClassifier.IsBoots(new BuildItemMetadata(
                        item.BuildsFrom,
                        item.BuildsInto,
                        item.Tags,
                        item.InStore,
                        item.PriceTotal)))
                .ToDictionary(
                    item => item.ItemId,
                    item => new ResourceMetadata(item.Name, item.Description));
        }

        return await context.RuneVersions
            .AsNoTracking()
            .Where(rune => rune.PatchVersion == patch && (resourceId == null || rune.RuneId == resourceId))
            .ToDictionaryAsync(
                rune => rune.RuneId,
                rune => new ResourceMetadata(rune.Name, rune.Description),
                ct);
    }

    private async Task<SnapshotSelection?> ResolveSnapshotAsync(string? requestedPatch, CancellationToken ct)
    {
        var explicitPatch = !string.IsNullOrWhiteSpace(requestedPatch);
        var desiredPatch = explicitPatch ? requestedPatch!.Trim() : await ResolveDefaultPatchAsync(ct);
        if (!string.IsNullOrWhiteSpace(desiredPatch))
        {
            var exact = await context.BuildResourceSnapshots.AsNoTracking()
                .Where(snapshot =>
                    snapshot.Patch == desiredPatch &&
                    snapshot.IsActive &&
                    snapshot.Status == BuildResourceSnapshotStatus.Ready)
                .OrderByDescending(snapshot => snapshot.CompletedAtUtc)
                .Select(snapshot => new SnapshotSelection(snapshot.Id, snapshot.Patch, snapshot.ProcessedMatchCount))
                .FirstOrDefaultAsync(ct);
            if (exact is not null || explicitPatch)
                return exact;
        }

        // During initial bootstrap or patch rollover, default reads stay on the newest completed
        // generation rather than invoking the raw corpus query or exposing a partial build.
        return await context.BuildResourceSnapshots.AsNoTracking()
            .Where(snapshot =>
                snapshot.IsActive &&
                snapshot.Status == BuildResourceSnapshotStatus.Ready)
            .OrderByDescending(snapshot => snapshot.CompletedAtUtc)
            .Select(snapshot => new SnapshotSelection(snapshot.Id, snapshot.Patch, snapshot.ProcessedMatchCount))
            .FirstOrDefaultAsync(ct);
    }

    private async Task<string> ResolveDefaultPatchAsync(CancellationToken ct)
    {
        var options = await patchQueryService.GetPatchOptionsAsync(QueueCatalog.QueueFamilyRankedSoloDuo, ct);
        return options.FirstOrDefault(option => option.IsActive && option.RankedSoloDuoMatchCount > 0)?.Patch
            ?? options.FirstOrDefault(option => option.RankedSoloDuoMatchCount > 0)?.Patch
            ?? string.Empty;
    }

    private static BuildResourceAnalyticsEntryDto BuildEntry(
        int resourceId,
        ResourceMetadata metadata,
        IReadOnlyList<ResourceAggregateRow> rows,
        IReadOnlyDictionary<ChampionRoleKey, int> championTotals,
        int totalParticipantGames,
        int championLimit)
    {
        var games = rows.Sum(row => row.Games);
        var wins = rows.Sum(row => row.Wins);
        return new BuildResourceAnalyticsEntryDto(
            resourceId,
            metadata.Name,
            metadata.Description,
            games,
            wins,
            games == 0 ? 0 : (double)wins / games,
            totalParticipantGames == 0 ? 0 : (double)games / totalParticipantGames,
            BuildChampionStats(rows, championTotals, games, championLimit));
    }

    private static IReadOnlyList<BuildResourceChampionStatDto> BuildChampionStats(
        IReadOnlyList<ResourceAggregateRow> rows,
        IReadOnlyDictionary<ChampionRoleKey, int> championTotals,
        int resourceGames,
        int limit) =>
        rows
            .OrderByDescending(row => row.Games)
            .ThenByDescending(row => row.Wins)
            .Take(limit)
            .Select(row =>
            {
                var championGames = championTotals.GetValueOrDefault(new ChampionRoleKey(row.ChampionId, row.Role));
                return new BuildResourceChampionStatDto(
                    row.ChampionId,
                    row.Role,
                    row.Games,
                    row.Wins,
                    row.Games == 0 ? 0 : (double)row.Wins / row.Games,
                    championGames == 0 ? 0 : (double)row.Games / championGames,
                    resourceGames == 0 ? 0 : (double)row.Games / resourceGames);
            })
            .ToList();

    private static BuildResourceAnalyticsIndexResponse EmptyIndex(
        string resourceType,
        string patch,
        string region) =>
        new(resourceType, patch, region, 0, []);

    private sealed record ResourceMetadata(string Name, string? Description);
    private readonly record struct ChampionRoleKey(int ChampionId, string Role);

    private sealed class ResourceAggregateRow
    {
        public int ResourceId { get; init; }
        public int ChampionId { get; init; }
        public string Role { get; init; } = string.Empty;
        public int Games { get; init; }
        public int Wins { get; init; }
    }

    private sealed class ChampionTotalRow
    {
        public int ChampionId { get; init; }
        public string Role { get; init; } = string.Empty;
        public int Games { get; init; }
    }

    private sealed record SnapshotData(
        List<ResourceAggregateRow> Aggregates,
        Dictionary<ChampionRoleKey, int> ChampionTotals,
        int TotalParticipantGames);

    private sealed record SnapshotSelection(Guid SnapshotId, string Patch, int ProcessedMatchCount)
    {
        public string Version => $"{SnapshotId}:{ProcessedMatchCount}";
    }
}
