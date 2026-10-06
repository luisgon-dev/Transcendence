using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Transcendence.Data;
using Transcendence.Data.Models.LoL.Analytics;
using Transcendence.Data.Models.LoL.Match;
using Transcendence.Service.Core.Services.Analytics.Interfaces;
using Transcendence.Service.Core.Services.Analytics.Models;
using Transcendence.Service.Core.Services.RiotApi;

namespace Transcendence.Service.Core.Services.Analytics.Implementations;

/// <summary>
/// Maintains Build Atlas generations in bounded match batches. A full rebuild (the first for a patch,
/// or forced) builds a new generation from the whole retained corpus and promotes it with a short
/// transaction; readers never observe Building/Failed generations. Incremental runs add only matches no
/// Ready/Retired generation has counted, straight into the active generation, one committed batch at a
/// time. A forced rebuild ignores the inclusion ledger and reconciles the full retained corpus.
/// </summary>
public sealed class BuildResourceSnapshotRefresher(
    TranscendenceContext context,
    IOptions<BuildResourceSnapshotOptions> optionsAccessor,
    ILogger<BuildResourceSnapshotRefresher> logger,
    IBuildResourceAnalyticsService? readCache = null) : IBuildResourceSnapshotRefresher
{
    private const int ParticipantChunkSize = 1_000;
    private const string ItemType = "item";
    private const string RuneType = "rune";
    private readonly BuildResourceSnapshotOptions options = optionsAccessor.Value;

    public async Task<BuildResourceSnapshotRefreshResult> RefreshAsync(
        string patch,
        bool forceFullRebuild,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(patch);

        var normalizedPatch = patch.Trim();
        var active = await context.BuildResourceSnapshots.AsNoTracking()
            .Where(snapshot =>
                snapshot.Patch == normalizedPatch &&
                snapshot.IsActive &&
                snapshot.Status == BuildResourceSnapshotStatus.Ready)
            .OrderByDescending(snapshot => snapshot.CompletedAtUtc)
            .FirstOrDefaultAsync(ct);

        var previousTimeout = context.Database.GetCommandTimeout();
        context.Database.SetCommandTimeout(Math.Clamp(options.CommandTimeoutSeconds, 30, 600));
        try
        {
            return active is not null && !forceFullRebuild
                ? await AddToActiveGenerationAsync(active, normalizedPatch, ct)
                : await RebuildGenerationAsync(normalizedPatch, ct);
        }
        finally
        {
            context.Database.SetCommandTimeout(previousTimeout);
        }
    }

    // Adds each batch of new matches to the active generation's counts in place: the changed and new
    // stat rows, the ledger rows and the generation's match count commit together, so a reader sees
    // every batch completely or not at all (the counts grow, as in Build Lab). Incremental runs used to
    // clone the whole generation (~228K rows on prod) into a new one every hour to add ~500 matches,
    // then delete an older one -- the second-largest source of dirtied pages on prod.
    private async Task<BuildResourceSnapshotRefreshResult> AddToActiveGenerationAsync(
        BuildResourceSnapshot active,
        string patch,
        CancellationToken ct)
    {
        var resources = (await context.BuildResourceStats.AsNoTracking()
                .Where(row => row.SnapshotId == active.Id)
                .ToListAsync(ct))
            .ToDictionary(row => new ResourceKey(
                row.PlatformRegion, row.ResourceType, row.ResourceId, row.ChampionId, row.Role));
        var populations = (await context.BuildResourcePopulationStats.AsNoTracking()
                .Where(row => row.SnapshotId == active.Id)
                .ToListAsync(ct))
            .ToDictionary(row => new PopulationKey(row.PlatformRegion, row.ChampionId, row.Role));
        var storedResources = resources.Keys.ToHashSet();
        var storedPopulations = populations.Keys.ToHashSet();
        var allowed = await LoadAllowedResourcesAsync(patch, ct);
        var batchSize = Math.Clamp(options.MatchBatchSize, 50, 2_000);
        var newlyProcessed = 0;

        while (true)
        {
            var matchIds = await LoadNextMatchBatchAsync(patch, active.Id, fullRebuild: false, batchSize, ct);
            if (matchIds.Count == 0)
                break;

            var touched = await CountBatchAsync(matchIds, patch, allowed, active.Id, resources, populations, ct);

            await using var transaction = await context.Database.BeginTransactionAsync(ct);
            foreach (var key in touched.Resources)
            {
                var row = resources[key];
                if (storedResources.Contains(key))
                    MarkCountsModified(context.Attach(row), stat => stat.Games, stat => stat.Wins);
                else
                    context.BuildResourceStats.Add(row);
            }
            foreach (var key in touched.Populations)
            {
                var row = populations[key];
                if (storedPopulations.Contains(key))
                    MarkCountsModified(context.Attach(row), stat => stat.Games);
                else
                    context.BuildResourcePopulationStats.Add(row);
            }
            context.BuildResourceProcessedMatches.AddRange(matchIds.Select(matchId =>
                new BuildResourceProcessedMatch { SnapshotId = active.Id, MatchId = matchId }));
            await context.SaveChangesAsync(ct);
            await context.BuildResourceSnapshots
                .Where(snapshot => snapshot.Id == active.Id)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(snapshot => snapshot.ProcessedMatchCount,
                        snapshot => snapshot.ProcessedMatchCount + matchIds.Count)
                    .SetProperty(snapshot => snapshot.CompletedAtUtc, DateTime.UtcNow), ct);
            await transaction.CommitAsync(ct);
            context.ChangeTracker.Clear();
            storedResources.UnionWith(touched.Resources);
            storedPopulations.UnionWith(touched.Populations);
            newlyProcessed += matchIds.Count;

            logger.LogInformation(
                "Build Atlas snapshot {SnapshotId} patch {Patch}: added {Processed} new matches in place ({Changed} stat rows written).",
                active.Id, patch, newlyProcessed, touched.Resources.Count + touched.Populations.Count);
        }

        if (newlyProcessed == 0)
        {
            logger.LogInformation(
                "Build Atlas patch {Patch} is current at snapshot {SnapshotId}; no new matches were eligible.",
                patch, active.Id);
        }

        await WarmReadCacheAsync(active.Id, patch, resources.Values, populations.Values, ct);

        return new BuildResourceSnapshotRefreshResult(
            active.Id, patch, false, newlyProcessed, resources.Count, populations.Count);
    }

    // Builds a new generation from the whole retained corpus and promotes it with a short transaction;
    // readers stay on the previous generation until then and never observe a partial or failed build.
    private async Task<BuildResourceSnapshotRefreshResult> RebuildGenerationAsync(string patch, CancellationToken ct)
    {
        var snapshot = new BuildResourceSnapshot
        {
            Id = Guid.NewGuid(),
            Patch = patch,
            Status = BuildResourceSnapshotStatus.Building,
            IsActive = false,
            IsFullRebuild = true,
            StartedAtUtc = DateTime.UtcNow,
            ProcessedMatchCount = 0
        };
        context.BuildResourceSnapshots.Add(snapshot);
        await context.SaveChangesAsync(ct);

        try
        {
            var resources = new Dictionary<ResourceKey, BuildResourceStat>();
            var populations = new Dictionary<PopulationKey, BuildResourcePopulationStat>();
            var allowed = await LoadAllowedResourcesAsync(patch, ct);
            var batchSize = Math.Clamp(options.MatchBatchSize, 50, 2_000);
            var newlyProcessed = 0;

            while (true)
            {
                var matchIds = await LoadNextMatchBatchAsync(patch, snapshot.Id, fullRebuild: true, batchSize, ct);
                if (matchIds.Count == 0)
                    break;

                await CountBatchAsync(matchIds, patch, allowed, snapshot.Id, resources, populations, ct);

                var ledgerRows = matchIds.Select(matchId => new BuildResourceProcessedMatch
                {
                    SnapshotId = snapshot.Id,
                    MatchId = matchId
                }).ToList();
                context.BuildResourceProcessedMatches.AddRange(ledgerRows);
                newlyProcessed += matchIds.Count;
                snapshot.ProcessedMatchCount += matchIds.Count;
                await context.SaveChangesAsync(ct);
                foreach (var row in ledgerRows)
                    context.Entry(row).State = EntityState.Detached;

                logger.LogInformation(
                    "Build Atlas snapshot {SnapshotId} patch {Patch}: processed {Processed} new matches ({Total} total source matches).",
                    snapshot.Id, patch, newlyProcessed, snapshot.ProcessedMatchCount);
            }

            context.BuildResourceStats.AddRange(resources.Values);
            context.BuildResourcePopulationStats.AddRange(populations.Values);
            await context.SaveChangesAsync(ct);
            context.ChangeTracker.Clear();

            await PromoteAsync(snapshot.Id, patch, ct);
            await CleanupPayloadsBestEffortAsync(patch, ct);
            await WarmReadCacheAsync(snapshot.Id, patch, resources.Values, populations.Values, ct);

            logger.LogInformation(
                "Build Atlas snapshot {SnapshotId} patch {Patch} promoted: full=True, newMatches={NewMatches}, resourceRows={ResourceRows}, populationRows={PopulationRows}.",
                snapshot.Id, patch, newlyProcessed, resources.Count, populations.Count);

            return new BuildResourceSnapshotRefreshResult(
                snapshot.Id, patch, true, newlyProcessed, resources.Count, populations.Count);
        }
        catch (Exception ex)
        {
            await MarkFailedAsync(snapshot.Id, ex, CancellationToken.None);
            throw;
        }
    }

    // Reads one batch's participants, items and runes and adds them to the given counts. Items and
    // runes are read 1,000 participants at a time: btree array lookups are costed per element, so a
    // whole batch (~5,000 ids) made the planner scan the 14 GB rune table (and the items) in full every
    // batch -- over half of prod's disk reads. At 1,000 ids, with the pinned participant cardinality,
    // it walks the primary key instead. Returns the keys whose counts changed.
    private async Task<TouchedKeys> CountBatchAsync(
        List<Guid> matchIds,
        string patch,
        AllowedResources allowed,
        Guid snapshotId,
        Dictionary<ResourceKey, BuildResourceStat> resources,
        Dictionary<PopulationKey, BuildResourcePopulationStat> populations,
        CancellationToken ct)
    {
        var touched = new TouchedKeys();
        var participants = await context.MatchParticipants.IgnoreQueryFilters().AsNoTracking()
            .Where(participant =>
                matchIds.Contains(participant.MatchId) &&
                participant.TeamPosition != null &&
                participant.TeamPosition != "")
            .Select(participant => new ParticipantRow
            {
                Id = participant.Id,
                Region = participant.Match.PlatformRegion ?? "",
                ChampionId = participant.ChampionId,
                Role = participant.TeamPosition!,
                Win = participant.Win
            })
            .ToListAsync(ct);
        touched.Populations.UnionWith(ApplyPopulationRows(populations, snapshotId, participants));

        var participantMap = participants.ToDictionary(participant => participant.Id);
        foreach (var chunk in participants.Select(participant => participant.Id).Chunk(ParticipantChunkSize))
        {
            if (allowed.ItemIds.Length > 0)
            {
                var itemUses = await context.MatchParticipantItems.IgnoreQueryFilters().AsNoTracking()
                    .Where(item =>
                        chunk.Contains(item.MatchParticipantId) &&
                        item.PatchVersion == patch &&
                        item.ItemId != 0 &&
                        allowed.ItemIds.Contains(item.ItemId))
                    .Select(item => new ResourceUseRow
                    {
                        ParticipantId = item.MatchParticipantId,
                        ResourceId = item.ItemId
                    })
                    .Distinct()
                    .ToListAsync(ct);
                touched.Resources.UnionWith(
                    ApplyResourceRows(resources, snapshotId, ItemType, itemUses, participantMap));
            }

            if (allowed.RuneIds.Length > 0)
            {
                var runeUses = await context.MatchParticipantRunes.IgnoreQueryFilters().AsNoTracking()
                    .Where(rune =>
                        chunk.Contains(rune.MatchParticipantId) &&
                        rune.PatchVersion == patch &&
                        rune.SelectionTree != RuneSelectionTree.StatShards &&
                        allowed.RuneIds.Contains(rune.RuneId))
                    .Select(rune => new ResourceUseRow
                    {
                        ParticipantId = rune.MatchParticipantId,
                        ResourceId = rune.RuneId
                    })
                    .Distinct()
                    .ToListAsync(ct);
                touched.Resources.UnionWith(
                    ApplyResourceRows(resources, snapshotId, RuneType, runeUses, participantMap));
            }
        }

        return touched;
    }

    private static void MarkCountsModified<T>(
        EntityEntry<T> entry,
        params Expression<Func<T, int>>[] counts) where T : class
    {
        foreach (var count in counts)
            entry.Property(count).IsModified = true;
    }

    private async Task<AllowedResources> LoadAllowedResourcesAsync(string patch, CancellationToken ct) =>
        new(
            await LoadAllowedItemIdsAsync(patch, ct),
            await context.RuneVersions.AsNoTracking()
                .Where(rune => rune.PatchVersion == patch)
                .Select(rune => rune.RuneId)
                .ToArrayAsync(ct));

    private async Task<List<Guid>> LoadNextMatchBatchAsync(
        string patch,
        Guid snapshotId,
        bool fullRebuild,
        int batchSize,
        CancellationToken ct)
    {
        var eligible = context.Matches.IgnoreQueryFilters().AsNoTracking()
            .Where(match =>
                match.Patch == patch &&
                match.Status == FetchStatus.Success &&
                (match.QueueId == QueueCatalog.RankedSoloDuoQueueId ||
                 (match.QueueId == 0 && match.QueueType == "420")));

        eligible = eligible.Where(match =>
            !context.BuildResourceProcessedMatches.Any(processed =>
                processed.SnapshotId == snapshotId && processed.MatchId == match.Id));

        if (!fullRebuild)
        {
            eligible = eligible.Where(match =>
                !context.BuildResourceProcessedMatches.Any(processed =>
                    processed.MatchId == match.Id &&
                    (processed.Snapshot.Status == BuildResourceSnapshotStatus.Ready ||
                     processed.Snapshot.Status == BuildResourceSnapshotStatus.Retired)));
        }

        return await eligible
            .OrderBy(match => match.FetchedAt)
            .ThenBy(match => match.MatchId)
            .Select(match => match.Id)
            .Take(batchSize)
            .ToListAsync(ct);
    }

    private async Task<int[]> LoadAllowedItemIdsAsync(string patch, CancellationToken ct)
    {
        var rows = await context.ItemVersions.AsNoTracking()
            .Where(item => item.PatchVersion == patch)
            .Select(item => new
            {
                item.ItemId, item.BuildsFrom, item.BuildsInto, item.Tags, item.InStore, item.PriceTotal
            })
            .ToListAsync(ct);
        return rows
            .Where(item =>
            {
                var metadata = new BuildItemMetadata(
                    item.BuildsFrom, item.BuildsInto, item.Tags, item.InStore, item.PriceTotal);
                return BuildItemClassifier.IsCompletedBuildItem(metadata) ||
                       BuildItemClassifier.IsBoots(metadata);
            })
            .Select(item => item.ItemId)
            .ToArray();
    }

    private static IEnumerable<PopulationKey> ApplyPopulationRows(
        Dictionary<PopulationKey, BuildResourcePopulationStat> rows,
        Guid snapshotId,
        IEnumerable<ParticipantRow> participants)
    {
        var changed = new List<PopulationKey>();
        foreach (var group in participants.GroupBy(participant =>
                     new PopulationKey(participant.Region, participant.ChampionId, participant.Role)))
        {
            if (!rows.TryGetValue(group.Key, out var row))
            {
                row = new BuildResourcePopulationStat
                {
                    Id = Guid.NewGuid(),
                    SnapshotId = snapshotId,
                    PlatformRegion = group.Key.Region,
                    ChampionId = group.Key.ChampionId,
                    Role = group.Key.Role
                };
                rows.Add(group.Key, row);
            }

            row.Games += group.Count();
            changed.Add(group.Key);
        }

        return changed;
    }

    private static IEnumerable<ResourceKey> ApplyResourceRows(
        Dictionary<ResourceKey, BuildResourceStat> rows,
        Guid snapshotId,
        string resourceType,
        IEnumerable<ResourceUseRow> uses,
        IReadOnlyDictionary<Guid, ParticipantRow> participants)
    {
        var changed = new List<ResourceKey>();
        var hydrated = uses
            .Where(use => participants.ContainsKey(use.ParticipantId))
            .Select(use => new { Use = use, Participant = participants[use.ParticipantId] });
        foreach (var group in hydrated.GroupBy(row => new ResourceKey(
                     row.Participant.Region,
                     resourceType,
                     row.Use.ResourceId,
                     row.Participant.ChampionId,
                     row.Participant.Role)))
        {
            if (!rows.TryGetValue(group.Key, out var stat))
            {
                stat = new BuildResourceStat
                {
                    Id = Guid.NewGuid(),
                    SnapshotId = snapshotId,
                    PlatformRegion = group.Key.Region,
                    ResourceType = group.Key.ResourceType,
                    ResourceId = group.Key.ResourceId,
                    ChampionId = group.Key.ChampionId,
                    Role = group.Key.Role
                };
                rows.Add(group.Key, stat);
            }

            stat.Games += group.Count();
            stat.Wins += group.Count(row => row.Participant.Win);
            changed.Add(group.Key);
        }

        return changed;
    }

    // Every run changes the generation version the read keys carry, so the item/rune pages would all
    // start cold. The counts are already in memory here; caching the pages from them costs no table
    // reads. Best effort: the generation is committed, and a failed warm only leaves reads to compute.
    private async Task WarmReadCacheAsync(
        Guid snapshotId,
        string patch,
        IEnumerable<BuildResourceStat> resources,
        IEnumerable<BuildResourcePopulationStat> populations,
        CancellationToken ct)
    {
        if (readCache is null)
            return;

        try
        {
            var processedMatchCount = await context.BuildResourceSnapshots.AsNoTracking()
                .Where(snapshot => snapshot.Id == snapshotId)
                .Select(snapshot => snapshot.ProcessedMatchCount)
                .SingleAsync(ct);
            await readCache.WarmGenerationAsync(
                snapshotId, patch, processedMatchCount, resources.ToList(), populations.ToList(), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex,
                "Build Atlas snapshot {SnapshotId} patch {Patch}: caching the item/rune pages failed; reads will compute on a miss.",
                snapshotId, patch);
        }
    }

    private async Task PromoteAsync(Guid snapshotId, string patch, CancellationToken ct)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(ct);
        await context.BuildResourceSnapshots
            .Where(snapshot => snapshot.Patch == patch && snapshot.IsActive)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(snapshot => snapshot.IsActive, false)
                .SetProperty(snapshot => snapshot.Status, BuildResourceSnapshotStatus.Retired), ct);
        await context.BuildResourceSnapshots
            .Where(snapshot => snapshot.Id == snapshotId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(snapshot => snapshot.IsActive, true)
                .SetProperty(snapshot => snapshot.Status, BuildResourceSnapshotStatus.Ready)
                .SetProperty(snapshot => snapshot.CompletedAtUtc, DateTime.UtcNow)
                .SetProperty(snapshot => snapshot.FailureReason, (string?)null), ct);
        await transaction.CommitAsync(ct);
    }

    private async Task MarkFailedAsync(Guid snapshotId, Exception exception, CancellationToken ct)
    {
        try
        {
            context.ChangeTracker.Clear();
            var failure = exception.GetBaseException().Message;
            if (failure.Length > 512)
                failure = failure[..512];
            await context.BuildResourceSnapshots
                .Where(snapshot => snapshot.Id == snapshotId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(snapshot => snapshot.IsActive, false)
                    .SetProperty(snapshot => snapshot.Status, BuildResourceSnapshotStatus.Failed)
                    .SetProperty(snapshot => snapshot.CompletedAtUtc, DateTime.UtcNow)
                    .SetProperty(snapshot => snapshot.FailureReason, failure), ct);
            await DeleteSnapshotPayloadAsync(snapshotId, deleteProcessedMatches: true, ct);
        }
        catch (Exception markFailure)
        {
            logger.LogError(markFailure,
                "Failed to mark Build Atlas snapshot {SnapshotId} as failed after refresh error.",
                snapshotId);
        }
    }

    private async Task CleanupPayloadsBestEffortAsync(string patch, CancellationToken ct)
    {
        try
        {
            await CleanupRetiredPayloadsAsync(patch, ct);
            await CleanupFailedPayloadsAsync(patch, ct);
        }
        catch (Exception cleanupFailure)
        {
            // Promotion has already committed. Cleanup is storage hygiene and must never demote a
            // successfully published generation or make the Hangfire job retry the completed work.
            logger.LogWarning(cleanupFailure,
                "Build Atlas snapshot payload cleanup failed for patch {Patch}; the Ready generation remains active.",
                patch);
        }
    }

    private async Task CleanupRetiredPayloadsAsync(string patch, CancellationToken ct)
    {
        var retained = await context.BuildResourceSnapshots.AsNoTracking()
            .Where(snapshot =>
                snapshot.Patch == patch &&
                (snapshot.Status == BuildResourceSnapshotStatus.Ready ||
                 snapshot.Status == BuildResourceSnapshotStatus.Retired))
            .OrderByDescending(snapshot => snapshot.CompletedAtUtc)
            .Select(snapshot => snapshot.Id)
            .Take(2)
            .ToListAsync(ct);
        var oldSnapshotIds = await context.BuildResourceSnapshots.AsNoTracking()
            .Where(snapshot =>
                snapshot.Patch == patch &&
                snapshot.Status == BuildResourceSnapshotStatus.Retired &&
                !retained.Contains(snapshot.Id))
            .Select(snapshot => snapshot.Id)
            .ToListAsync(ct);
        if (oldSnapshotIds.Count == 0)
            return;

        foreach (var snapshotId in oldSnapshotIds)
            await DeleteSnapshotPayloadAsync(snapshotId, deleteProcessedMatches: false, ct);
    }

    private async Task CleanupFailedPayloadsAsync(string patch, CancellationToken ct)
    {
        var failedSnapshotIds = await context.BuildResourceSnapshots.AsNoTracking()
            .Where(snapshot =>
                snapshot.Patch == patch &&
                snapshot.Status == BuildResourceSnapshotStatus.Failed)
            .Select(snapshot => snapshot.Id)
            .ToListAsync(ct);

        foreach (var snapshotId in failedSnapshotIds)
            await DeleteSnapshotPayloadAsync(snapshotId, deleteProcessedMatches: true, ct);
    }

    private async Task DeleteSnapshotPayloadAsync(
        Guid snapshotId,
        bool deleteProcessedMatches,
        CancellationToken ct)
    {
        await context.BuildResourceStats
            .Where(row => row.SnapshotId == snapshotId)
            .ExecuteDeleteAsync(ct);
        await context.BuildResourcePopulationStats
            .Where(row => row.SnapshotId == snapshotId)
            .ExecuteDeleteAsync(ct);
        if (deleteProcessedMatches)
        {
            await context.BuildResourceProcessedMatches
                .Where(row => row.SnapshotId == snapshotId)
                .ExecuteDeleteAsync(ct);
        }
    }

    private readonly record struct ResourceKey(
        string Region,
        string ResourceType,
        int ResourceId,
        int ChampionId,
        string Role);

    private readonly record struct PopulationKey(string Region, int ChampionId, string Role);

    private sealed record AllowedResources(int[] ItemIds, int[] RuneIds);

    private sealed class TouchedKeys
    {
        public HashSet<ResourceKey> Resources { get; } = [];
        public HashSet<PopulationKey> Populations { get; } = [];
    }

    private sealed class ParticipantRow
    {
        public Guid Id { get; init; }
        public string Region { get; init; } = "";
        public int ChampionId { get; init; }
        public string Role { get; init; } = "";
        public bool Win { get; init; }
    }

    private sealed class ResourceUseRow
    {
        public Guid ParticipantId { get; init; }
        public int ResourceId { get; init; }
    }
}
