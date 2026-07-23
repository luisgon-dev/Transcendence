using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Transcendence.Data;
using Transcendence.Data.Models.LoL.Match;
using Transcendence.Service.Core.Queries;
using Transcendence.Service.Core.Services.Analytics.Interfaces;
using Transcendence.Service.Core.Services.Analytics.Models;
using Transcendence.Service.Core.Services.Cache;
using Transcendence.Service.Core.Services.RiotApi;

namespace Transcendence.Service.Core.Services.Analytics.Implementations;

/// <summary>
/// Cached, corpus-backed item and rune exploration. The aggregation starts from one row per
/// participant/resource so duplicate inventory slots never inflate pick rate.
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
        var patch = await ResolvePatchAsync(requestedPatch, ct);
        var normalizedRegion = AnalyticsRegionCatalog.NormalizeOrDefault(region);
        if (string.IsNullOrEmpty(patch))
            return EmptyIndex(resourceType, normalizedRegion);

        var key = $"analytics:build-resources:v1:{resourceType}:{patch}:{normalizedRegion}";
        return await cache.GetOrCreateAsync(
            key,
            cancel => ComputeIndexAsync(resourceType, normalizedRegion, patch, cancel),
            CacheOptions,
            tags: ["analytics", CacheTags.ForPatch(patch)],
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

        var patch = await ResolvePatchAsync(requestedPatch, ct);
        var normalizedRegion = AnalyticsRegionCatalog.NormalizeOrDefault(region);
        if (string.IsNullOrEmpty(patch))
            return null;

        var key = $"analytics:build-resource:v1:{resourceType}:{resourceId}:{patch}:{normalizedRegion}";
        return await cache.GetOrCreateAsync(
            key,
            cancel => ComputeDetailAsync(resourceType, resourceId, normalizedRegion, patch, cancel),
            CacheOptions,
            tags: ["analytics", CacheTags.ForPatch(patch)],
            cancellationToken: ct);
    }

    private async ValueTask<BuildResourceAnalyticsIndexResponse> ComputeIndexAsync(
        string resourceType,
        string region,
        string patch,
        CancellationToken ct)
    {
        var metadata = await LoadMetadataAsync(resourceType, patch, ct);
        if (metadata.Count == 0)
            return new BuildResourceAnalyticsIndexResponse(resourceType, patch, region, 0, []);

        var precomputed = await LoadPrecomputedAsync(resourceType, region, patch, metadata.Keys.ToArray(), ct);
        if (precomputed != null)
        {
            var precomputedEntries = precomputed.Aggregates
                .GroupBy(row => row.ResourceId)
                .Select(group => BuildEntry(
                    group.Key,
                    metadata[group.Key],
                    group.ToList(),
                    precomputed.ChampionTotals,
                    precomputed.TotalParticipantGames,
                    IndexChampionLimit))
                .OrderByDescending(entry => entry.Games)
                .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            return new BuildResourceAnalyticsIndexResponse(
                resourceType, patch, region, precomputed.TotalParticipantGames, precomputedEntries);
        }

        var participants = BuildParticipantQuery(region, patch);
        var totalParticipantGames = await participants.CountAsync(ct);
        if (totalParticipantGames == 0)
            return new BuildResourceAnalyticsIndexResponse(resourceType, patch, region, 0, []);

        var championTotals = await LoadChampionTotalsAsync(participants, ct);
        var aggregates = await LoadResourceAggregatesAsync(
            participants,
            resourceType,
            patch,
            metadata.Keys.ToArray(),
            resourceId: null,
            ct);

        var entries = aggregates
            .GroupBy(row => row.ResourceId)
            .Select(group => BuildEntry(
                group.Key,
                metadata[group.Key],
                group.ToList(),
                championTotals,
                totalParticipantGames,
                IndexChampionLimit))
            .OrderByDescending(entry => entry.Games)
            .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new BuildResourceAnalyticsIndexResponse(
            resourceType,
            patch,
            region,
            totalParticipantGames,
            entries);
    }

    private async ValueTask<BuildResourceAnalyticsDetailResponse?> ComputeDetailAsync(
        string resourceType,
        int resourceId,
        string region,
        string patch,
        CancellationToken ct)
    {
        var metadata = await LoadMetadataAsync(resourceType, patch, ct, resourceId);
        if (!metadata.TryGetValue(resourceId, out var resourceMetadata))
            return null;

        var precomputed = await LoadPrecomputedAsync(resourceType, region, patch, [resourceId], ct);
        if (precomputed != null)
        {
            if (precomputed.Aggregates.Count == 0)
                return null;
            var precomputedEntry = BuildEntry(
                resourceId,
                resourceMetadata,
                precomputed.Aggregates,
                precomputed.ChampionTotals,
                precomputed.TotalParticipantGames,
                DetailChampionLimit);
            return new BuildResourceAnalyticsDetailResponse(
                resourceType,
                patch,
                region,
                precomputed.TotalParticipantGames,
                precomputedEntry,
                BuildChampionStats(
                    precomputed.Aggregates,
                    precomputed.ChampionTotals,
                    precomputedEntry.Games,
                    DetailChampionLimit));
        }

        var participants = BuildParticipantQuery(region, patch);
        var totalParticipantGames = await participants.CountAsync(ct);
        if (totalParticipantGames == 0)
            return null;

        var championTotals = await LoadChampionTotalsAsync(participants, ct);
        var aggregates = await LoadResourceAggregatesAsync(
            participants,
            resourceType,
            patch,
            [resourceId],
            resourceId,
            ct);
        if (aggregates.Count == 0)
            return null;

        var entry = BuildEntry(
            resourceId,
            resourceMetadata,
            aggregates,
            championTotals,
            totalParticipantGames,
            DetailChampionLimit);
        var championStats = BuildChampionStats(
            aggregates,
            championTotals,
            entry.Games,
            DetailChampionLimit);

        return new BuildResourceAnalyticsDetailResponse(
            resourceType,
            patch,
            region,
            totalParticipantGames,
            entry,
            championStats);
    }

    private IQueryable<MatchParticipant> BuildParticipantQuery(string region, string patch) =>
        context.MatchParticipants
            // Every match constraint required by this analytics surface is applied explicitly below.
            // Ignoring the dependent global filters prevents EF from injecting an additional
            // Matches join before applying the same successful-match predicate.
            .IgnoreQueryFilters()
            .AsNoTracking()
            .OnPatch(patch)
            .FromSuccessfulMatches()
            .InAnalyticsQueue(QueueCatalog.QueueFamilyRankedSoloDuo)
            .InPlatformRegion(AnalyticsRegionCatalog.NormalizeToFilter(region))
            .WithAssignedRole();

    private static async Task<Dictionary<ChampionRoleKey, int>> LoadChampionTotalsAsync(
        IQueryable<MatchParticipant> participants,
        CancellationToken ct)
    {
        var rows = await participants
            .GroupBy(participant => new { participant.ChampionId, Role = participant.TeamPosition! })
            .Select(group => new ChampionTotalRow
            {
                ChampionId = group.Key.ChampionId,
                Role = group.Key.Role,
                Games = group.Count()
            })
            .ToListAsync(ct);

        return rows.ToDictionary(row => new ChampionRoleKey(row.ChampionId, row.Role), row => row.Games);
    }

    private async Task<PrecomputedResourceData?> LoadPrecomputedAsync(
        string resourceType,
        string region,
        string patch,
        int[] allowedIds,
        CancellationToken ct)
    {
        var hasSnapshot = await context.BuildResourceStats.AsNoTracking()
            .AnyAsync(row => row.Patch == patch && row.ResourceType == resourceType, ct);
        if (!hasSnapshot)
            return null;

        var regionFilter = AnalyticsRegionCatalog.NormalizeToFilter(region);
        var stats = context.BuildResourceStats.AsNoTracking()
            .Where(row => row.Patch == patch &&
                          row.ResourceType == resourceType &&
                          allowedIds.Contains(row.ResourceId));
        var population = context.ChampionRoleTierStats.AsNoTracking()
            .Where(row => row.Patch == patch &&
                          row.QueueFamily == QueueCatalog.QueueFamilyRankedSoloDuo);
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
        return new PrecomputedResourceData(aggregates, championTotals, championRows.Sum(row => row.Games));
    }

    private async Task<List<ResourceAggregateRow>> LoadResourceAggregatesAsync(
        IQueryable<MatchParticipant> participants,
        string resourceType,
        string patch,
        int[] allowedIds,
        int? resourceId,
        CancellationToken ct)
    {
        IQueryable<ResourceUseRow> uses;
        if (resourceType == ItemType)
        {
            uses = context.MatchParticipantItems
                // The participant query already excludes every ineligible match. Applying the
                // dependent global filter here makes EF scan a second participant/match tree across
                // the historical resource table before joining it back to the current patch.
                .IgnoreQueryFilters()
                .Where(item =>
                        item.PatchVersion == patch &&
                        item.ItemId != 0 &&
                        allowedIds.Contains(item.ItemId) &&
                        (resourceId == null || item.ItemId == resourceId))
                .Join(
                    participants,
                    item => item.MatchParticipantId,
                    participant => participant.Id,
                    (item, participant) => new ResourceUseRow
                    {
                        ResourceId = item.ItemId,
                        ParticipantId = participant.Id,
                        ChampionId = participant.ChampionId,
                        Role = participant.TeamPosition!,
                        Win = participant.Win
                    })
                .Distinct();
        }
        else
        {
            uses = context.MatchParticipantRunes
                .IgnoreQueryFilters()
                .Where(rune =>
                        rune.PatchVersion == patch &&
                        rune.SelectionTree != RuneSelectionTree.StatShards &&
                        allowedIds.Contains(rune.RuneId) &&
                        (resourceId == null || rune.RuneId == resourceId))
                .Join(
                    participants,
                    rune => rune.MatchParticipantId,
                    participant => participant.Id,
                    (rune, participant) => new ResourceUseRow
                    {
                        ResourceId = rune.RuneId,
                        ParticipantId = participant.Id,
                        ChampionId = participant.ChampionId,
                        Role = participant.TeamPosition!,
                        Win = participant.Win
                    })
                .Distinct();
        }

        return await uses
            .GroupBy(row => new { row.ResourceId, row.ChampionId, row.Role })
            .Select(group => new ResourceAggregateRow
            {
                ResourceId = group.Key.ResourceId,
                ChampionId = group.Key.ChampionId,
                Role = group.Key.Role,
                Games = group.Count(),
                Wins = group.Count(row => row.Win)
            })
            .ToListAsync(ct);
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

    private async Task<string> ResolvePatchAsync(string? requestedPatch, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(requestedPatch))
            return requestedPatch.Trim();

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

    private static BuildResourceAnalyticsIndexResponse EmptyIndex(string resourceType, string region) =>
        new(resourceType, string.Empty, region, 0, []);

    private sealed record ResourceMetadata(string Name, string? Description);
    private readonly record struct ChampionRoleKey(int ChampionId, string Role);

    private sealed class ResourceUseRow
    {
        public int ResourceId { get; init; }
        public Guid ParticipantId { get; init; }
        public int ChampionId { get; init; }
        public string Role { get; init; } = string.Empty;
        public bool Win { get; init; }
    }

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

    private sealed record PrecomputedResourceData(
        List<ResourceAggregateRow> Aggregates,
        Dictionary<ChampionRoleKey, int> ChampionTotals,
        int TotalParticipantGames);
}
