using Microsoft.EntityFrameworkCore;
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
/// Builds immutable Build Atlas generations in bounded match batches. A completed generation is
/// promoted with a short transaction; readers never observe Building/Failed generations.
/// Incremental runs clone the active atoms and process only matches not included by a Ready/Retired
/// generation. A forced rebuild ignores the inclusion ledger and reconciles the full retained corpus.
/// </summary>
public sealed class BuildResourceSnapshotRefresher(
    TranscendenceContext context,
    IOptions<BuildResourceSnapshotOptions> optionsAccessor,
    ILogger<BuildResourceSnapshotRefresher> logger) : IBuildResourceSnapshotRefresher
{
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
        var fullRebuild = forceFullRebuild || active is null;
        var snapshot = new BuildResourceSnapshot
        {
            Id = Guid.NewGuid(),
            Patch = normalizedPatch,
            Status = BuildResourceSnapshotStatus.Building,
            IsActive = false,
            IsFullRebuild = fullRebuild,
            StartedAtUtc = DateTime.UtcNow,
            ProcessedMatchCount = fullRebuild ? 0 : active!.ProcessedMatchCount
        };
        context.BuildResourceSnapshots.Add(snapshot);
        await context.SaveChangesAsync(ct);

        var previousTimeout = context.Database.GetCommandTimeout();
        context.Database.SetCommandTimeout(Math.Clamp(options.CommandTimeoutSeconds, 30, 600));
        try
        {
            var resources = fullRebuild
                ? new Dictionary<ResourceKey, BuildResourceStat>()
                : await CloneResourceStatsAsync(active!.Id, snapshot.Id, ct);
            var populations = fullRebuild
                ? new Dictionary<PopulationKey, BuildResourcePopulationStat>()
                : await ClonePopulationStatsAsync(active!.Id, snapshot.Id, ct);
            var allowedItemIds = await LoadAllowedItemIdsAsync(normalizedPatch, ct);
            var allowedRuneIds = await context.RuneVersions.AsNoTracking()
                .Where(rune => rune.PatchVersion == normalizedPatch)
                .Select(rune => rune.RuneId)
                .ToArrayAsync(ct);
            var batchSize = Math.Clamp(options.MatchBatchSize, 50, 2_000);
            var newlyProcessed = 0;

            while (true)
            {
                var matchIds = await LoadNextMatchBatchAsync(
                    normalizedPatch, snapshot.Id, fullRebuild, batchSize, ct);
                if (matchIds.Count == 0)
                    break;

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
                ApplyPopulationRows(populations, snapshot.Id, participants);

                var participantIds = participants.Select(participant => participant.Id).ToArray();
                if (participantIds.Length > 0)
                {
                    var participantMap = participants.ToDictionary(participant => participant.Id);
                    if (allowedItemIds.Length > 0)
                    {
                        var itemUses = await context.MatchParticipantItems.IgnoreQueryFilters().AsNoTracking()
                            .Where(item =>
                                participantIds.Contains(item.MatchParticipantId) &&
                                item.PatchVersion == normalizedPatch &&
                                item.ItemId != 0 &&
                                allowedItemIds.Contains(item.ItemId))
                            .Select(item => new ResourceUseRow
                            {
                                ParticipantId = item.MatchParticipantId,
                                ResourceId = item.ItemId
                            })
                            .Distinct()
                            .ToListAsync(ct);
                        ApplyResourceRows(resources, snapshot.Id, ItemType, itemUses, participantMap);
                    }

                    if (allowedRuneIds.Length > 0)
                    {
                        var runeUses = await context.MatchParticipantRunes.IgnoreQueryFilters().AsNoTracking()
                            .Where(rune =>
                                participantIds.Contains(rune.MatchParticipantId) &&
                                rune.PatchVersion == normalizedPatch &&
                                rune.SelectionTree != RuneSelectionTree.StatShards &&
                                allowedRuneIds.Contains(rune.RuneId))
                            .Select(rune => new ResourceUseRow
                            {
                                ParticipantId = rune.MatchParticipantId,
                                ResourceId = rune.RuneId
                            })
                            .Distinct()
                            .ToListAsync(ct);
                        ApplyResourceRows(resources, snapshot.Id, RuneType, runeUses, participantMap);
                    }
                }

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
                    snapshot.Id, normalizedPatch, newlyProcessed, snapshot.ProcessedMatchCount);
            }

            if (!fullRebuild && newlyProcessed == 0)
            {
                context.ChangeTracker.Clear();
                await context.BuildResourceSnapshots
                    .Where(candidate => candidate.Id == snapshot.Id)
                    .ExecuteDeleteAsync(ct);
                logger.LogInformation(
                    "Build Atlas patch {Patch} is current at snapshot {SnapshotId}; no new matches were eligible.",
                    normalizedPatch, active!.Id);
                return new BuildResourceSnapshotRefreshResult(
                    active.Id,
                    normalizedPatch,
                    false,
                    0,
                    resources.Count,
                    populations.Count);
            }

            context.BuildResourceStats.AddRange(resources.Values);
            context.BuildResourcePopulationStats.AddRange(populations.Values);
            await context.SaveChangesAsync(ct);
            context.ChangeTracker.Clear();

            await PromoteAsync(snapshot.Id, normalizedPatch, ct);
            await CleanupPayloadsBestEffortAsync(normalizedPatch, ct);

            logger.LogInformation(
                "Build Atlas snapshot {SnapshotId} patch {Patch} promoted: full={Full}, newMatches={NewMatches}, resourceRows={ResourceRows}, populationRows={PopulationRows}.",
                snapshot.Id, normalizedPatch, fullRebuild, newlyProcessed, resources.Count, populations.Count);

            return new BuildResourceSnapshotRefreshResult(
                snapshot.Id,
                normalizedPatch,
                fullRebuild,
                newlyProcessed,
                resources.Count,
                populations.Count);
        }
        catch (Exception ex)
        {
            await MarkFailedAsync(snapshot.Id, ex, CancellationToken.None);
            throw;
        }
        finally
        {
            context.Database.SetCommandTimeout(previousTimeout);
        }
    }

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

    private async Task<Dictionary<ResourceKey, BuildResourceStat>> CloneResourceStatsAsync(
        Guid sourceSnapshotId,
        Guid targetSnapshotId,
        CancellationToken ct)
    {
        var rows = await context.BuildResourceStats.AsNoTracking()
            .Where(row => row.SnapshotId == sourceSnapshotId)
            .ToListAsync(ct);
        return rows.ToDictionary(
            row => new ResourceKey(
                row.PlatformRegion, row.ResourceType, row.ResourceId, row.ChampionId, row.Role),
            row => new BuildResourceStat
            {
                Id = Guid.NewGuid(),
                SnapshotId = targetSnapshotId,
                PlatformRegion = row.PlatformRegion,
                ResourceType = row.ResourceType,
                ResourceId = row.ResourceId,
                ChampionId = row.ChampionId,
                Role = row.Role,
                Games = row.Games,
                Wins = row.Wins
            });
    }

    private async Task<Dictionary<PopulationKey, BuildResourcePopulationStat>> ClonePopulationStatsAsync(
        Guid sourceSnapshotId,
        Guid targetSnapshotId,
        CancellationToken ct)
    {
        var rows = await context.BuildResourcePopulationStats.AsNoTracking()
            .Where(row => row.SnapshotId == sourceSnapshotId)
            .ToListAsync(ct);
        return rows.ToDictionary(
            row => new PopulationKey(row.PlatformRegion, row.ChampionId, row.Role),
            row => new BuildResourcePopulationStat
            {
                Id = Guid.NewGuid(),
                SnapshotId = targetSnapshotId,
                PlatformRegion = row.PlatformRegion,
                ChampionId = row.ChampionId,
                Role = row.Role,
                Games = row.Games
            });
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

    private static void ApplyPopulationRows(
        Dictionary<PopulationKey, BuildResourcePopulationStat> rows,
        Guid snapshotId,
        IEnumerable<ParticipantRow> participants)
    {
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
        }
    }

    private static void ApplyResourceRows(
        Dictionary<ResourceKey, BuildResourceStat> rows,
        Guid snapshotId,
        string resourceType,
        IEnumerable<ResourceUseRow> uses,
        IReadOnlyDictionary<Guid, ParticipantRow> participants)
    {
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
