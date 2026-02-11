using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Transcendence.Data;
using Transcendence.Data.Models.LoL.Match;
using Transcendence.Service.Core.Services.Jobs.Configuration;

namespace Transcendence.Service.Core.Services.Jobs;

[DisableConcurrentExecution(timeoutInSeconds: 10 * 60)]
public class RuneSelectionIntegrityBackfillJob(
    TranscendenceContext db,
    IOptions<RuneSelectionIntegrityBackfillJobOptions> options,
    ILogger<RuneSelectionIntegrityBackfillJob> logger)
{
    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        var batchSize = Math.Max(25, options.Value.BatchSize);
        var maxBatches = Math.Max(1, options.Value.MaxBatchesPerRun);

        var totalParticipantsUpdated = 0;
        var totalRunesUpdated = 0;
        var batchesProcessed = 0;

        while (!ct.IsCancellationRequested && batchesProcessed < maxBatches)
        {
            var participantIds = await db.MatchParticipantRunes
                .AsNoTracking()
                .GroupBy(r => r.MatchParticipantId)
                .Where(g => g.All(r =>
                    r.StyleId == 0 &&
                    r.SelectionTree == RuneSelectionTree.Primary &&
                    r.SelectionIndex == 0))
                .OrderBy(g => g.Key)
                .Select(g => g.Key)
                .Take(batchSize)
                .ToListAsync(ct);

            if (participantIds.Count == 0)
                break;

            var runes = await db.MatchParticipantRunes
                .Where(r => participantIds.Contains(r.MatchParticipantId))
                .OrderBy(r => r.MatchParticipantId)
                .ThenBy(r => r.RuneId)
                .ToListAsync(ct);

            var runeIds = runes.Select(r => r.RuneId).Distinct().ToList();
            var patches = runes.Select(r => NormalizePatchVersion(r.PatchVersion)).Distinct().ToList();

            var runeMetadataRows = await db.RuneVersions
                .AsNoTracking()
                .Where(rv => runeIds.Contains(rv.RuneId) && patches.Contains(rv.PatchVersion))
                .Select(rv => new { rv.RuneId, rv.PatchVersion, rv.RunePathId, rv.Slot })
                .ToListAsync(ct);

            var metadataByPatch = runeMetadataRows
                .GroupBy(rv => new RunePatchKey(rv.RuneId, NormalizePatchVersion(rv.PatchVersion)))
                .ToDictionary(g => g.Key, g =>
                {
                    var row = g.First();
                    return new RuneMetadata(row.RunePathId, row.Slot);
                });
            var metadataByRuneId = runeMetadataRows
                .GroupBy(rv => rv.RuneId)
                .ToDictionary(g => g.Key, g =>
                {
                    var row = g.First();
                    return new RuneMetadata(row.RunePathId, row.Slot);
                });

            var runesUpdatedInBatch = 0;
            foreach (var group in runes.GroupBy(r => r.MatchParticipantId))
            {
                var updates = BuildSelectionUpdates(group.ToList(), metadataByPatch, metadataByRuneId);
                foreach (var update in updates)
                {
                    if (update.Row.SelectionTree != update.SelectionTree ||
                        update.Row.SelectionIndex != update.SelectionIndex ||
                        update.Row.StyleId != update.StyleId)
                    {
                        update.Row.SelectionTree = update.SelectionTree;
                        update.Row.SelectionIndex = update.SelectionIndex;
                        update.Row.StyleId = update.StyleId;
                        runesUpdatedInBatch++;
                    }
                }
            }

            await db.SaveChangesAsync(ct);

            batchesProcessed++;
            totalParticipantsUpdated += participantIds.Count;
            totalRunesUpdated += runesUpdatedInBatch;

            logger.LogInformation(
                "[RuneSelectionIntegrityBackfill] Batch {BatchNumber}/{MaxBatches}: participants={ParticipantCount}, runesUpdated={RunesUpdated}.",
                batchesProcessed,
                maxBatches,
                participantIds.Count,
                runesUpdatedInBatch);
        }

        logger.LogInformation(
            "[RuneSelectionIntegrityBackfill] Completed. batches={BatchesProcessed}, participants={ParticipantsUpdated}, runesUpdated={RunesUpdated}.",
            batchesProcessed,
            totalParticipantsUpdated,
            totalRunesUpdated);
    }

    private static List<RuneSelectionUpdate> BuildSelectionUpdates(
        List<MatchParticipantRune> runes,
        IReadOnlyDictionary<RunePatchKey, RuneMetadata> metadataByPatch,
        IReadOnlyDictionary<int, RuneMetadata> metadataByRuneId)
    {
        var resolved = runes
            .Select(row =>
            {
                var patch = NormalizePatchVersion(row.PatchVersion);
                var hasMetadata = metadataByPatch.TryGetValue(new RunePatchKey(row.RuneId, patch), out var metadata) ||
                                  metadataByRuneId.TryGetValue(row.RuneId, out metadata);

                return new ResolvedRuneRow(
                    row,
                    hasMetadata ? metadata.PathId : 0,
                    hasMetadata ? metadata.Slot : int.MaxValue);
            })
            .ToList();

        var primaryPathId = resolved
            .Where(r => r.PathId is > 0 and < 5000)
            .GroupBy(r => r.PathId)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key)
            .Select(g => g.Key)
            .FirstOrDefault();

        var secondaryPathId = resolved
            .Where(r => r.PathId is > 0 and < 5000 && r.PathId != primaryPathId)
            .GroupBy(r => r.PathId)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key)
            .Select(g => g.Key)
            .FirstOrDefault();

        var provisional = new List<ProvisionalRuneRow>(resolved.Count);
        foreach (var row in resolved)
        {
            if (row.PathId >= 5000)
            {
                provisional.Add(new ProvisionalRuneRow(row, RuneSelectionTree.StatShards, 0));
                continue;
            }

            if (row.PathId > 0 && row.PathId == primaryPathId)
            {
                provisional.Add(new ProvisionalRuneRow(row, RuneSelectionTree.Primary, primaryPathId));
                continue;
            }

            if (row.PathId > 0)
            {
                provisional.Add(new ProvisionalRuneRow(row, RuneSelectionTree.Secondary, row.PathId));
                continue;
            }

            provisional.Add(new ProvisionalRuneRow(row, null, 0));
        }

        var assignedPrimaryCount = provisional.Count(r => r.SelectionTree == RuneSelectionTree.Primary);
        foreach (var row in provisional.Where(r => r.SelectionTree == null).OrderBy(r => r.Resolved.Slot).ThenBy(r => r.Resolved.Row.RuneId))
        {
            var assignPrimary = assignedPrimaryCount < 4;
            row.SelectionTree = assignPrimary ? RuneSelectionTree.Primary : RuneSelectionTree.Secondary;
            row.StyleId = assignPrimary ? primaryPathId : secondaryPathId;
            if (assignPrimary) assignedPrimaryCount++;
        }

        var updates = new List<RuneSelectionUpdate>(provisional.Count);
        foreach (var treeGroup in provisional
                     .Where(r => r.SelectionTree.HasValue)
                     .GroupBy(r => r.SelectionTree!.Value))
        {
            var ordered = treeGroup
                .OrderBy(r => r.Resolved.Slot)
                .ThenBy(r => r.Resolved.Row.RuneId)
                .ToList();

            for (var i = 0; i < ordered.Count; i++)
            {
                var row = ordered[i];
                updates.Add(new RuneSelectionUpdate(
                    row.Resolved.Row,
                    row.SelectionTree!.Value,
                    i,
                    row.SelectionTree == RuneSelectionTree.StatShards ? 0 : row.StyleId));
            }
        }

        return updates;
    }

    private static string NormalizePatchVersion(string? patchVersion) =>
        string.IsNullOrWhiteSpace(patchVersion) ? string.Empty : patchVersion.Trim();

    private readonly record struct RunePatchKey(int RuneId, string PatchVersion);
    private readonly record struct RuneMetadata(int PathId, int Slot);
    private readonly record struct ResolvedRuneRow(MatchParticipantRune Row, int PathId, int Slot);

    private sealed class ProvisionalRuneRow(ResolvedRuneRow resolved, RuneSelectionTree? selectionTree, int styleId)
    {
        public ResolvedRuneRow Resolved { get; } = resolved;
        public RuneSelectionTree? SelectionTree { get; set; } = selectionTree;
        public int StyleId { get; set; } = styleId;
    }

    private readonly record struct RuneSelectionUpdate(
        MatchParticipantRune Row,
        RuneSelectionTree SelectionTree,
        int SelectionIndex,
        int StyleId);
}
