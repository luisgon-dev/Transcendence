using Transcendence.Data.Models.LoL.Match;

namespace Transcendence.Service.Core.Services.StaticData.Models;

internal readonly record struct StoredRuneSelection(
    int RuneId,
    RuneSelectionTree SelectionTree,
    int SelectionIndex,
    int StyleId);

internal readonly record struct RuneSelectionMetadata(int PathId, int Slot);

internal sealed record MappedRuneSelection(
    int PrimaryStyleId,
    int SubStyleId,
    List<int> PrimaryRunes,
    List<int> SubRunes,
    List<int> StatShards);

/// <summary>
/// Maps persisted rune rows into one stable primary/secondary/stat-shard shape.
/// Explicit hierarchy wins; legacy rows fall back to static rune metadata.
/// </summary>
internal static class RuneSelectionMapper
{
    internal static MappedRuneSelection Map(
        IReadOnlyList<StoredRuneSelection> selections,
        Func<int, RuneSelectionMetadata?> resolveMetadata)
    {
        if (selections.Count == 0)
            return new MappedRuneSelection(0, 0, [], [], []);

        if (HasStructuredSelections(selections))
        {
            var primaryRunes = SelectTree(selections, RuneSelectionTree.Primary);
            var subRunes = SelectTree(selections, RuneSelectionTree.Secondary);
            var statShards = SelectTree(selections, RuneSelectionTree.StatShards);

            var primaryStyleId = SelectStyle(selections, RuneSelectionTree.Primary);
            var subStyleId = SelectStyle(selections, RuneSelectionTree.Secondary);

            if (primaryStyleId == 0 && primaryRunes.Count > 0)
                primaryStyleId = ResolveRealPath(primaryRunes[0], resolveMetadata);

            if (subStyleId == 0 && subRunes.Count > 0)
                subStyleId = ResolveRealPath(subRunes[0], resolveMetadata);

            return new MappedRuneSelection(
                primaryStyleId,
                subStyleId,
                primaryRunes,
                subRunes,
                statShards);
        }

        var resolvedRunes = selections
            .Select(selection =>
            {
                var metadata = resolveMetadata(selection.RuneId);
                return metadata.HasValue
                    ? (selection.RuneId, metadata.Value.PathId, metadata.Value.Slot)
                    : (selection.RuneId, PathId: 0, Slot: selection.SelectionIndex);
            })
            .ToList();

        var statShardsFallback = resolvedRunes
            .Where(rune => RunePathIds.IsStatModPath(rune.PathId))
            .OrderBy(rune => rune.Slot)
            .Select(rune => rune.RuneId)
            .Take(3)
            .ToList();

        var realPaths = resolvedRunes
            .Where(rune => RunePathIds.IsRealRunePath(rune.PathId))
            .GroupBy(rune => rune.PathId)
            .Select(group => new
            {
                PathId = group.Key,
                Runes = group.OrderBy(rune => rune.Slot).Select(rune => rune.RuneId).ToList()
            })
            .OrderByDescending(path => path.Runes.Count)
            .ThenBy(path => path.PathId)
            .ToList();

        var primaryPath = realPaths.FirstOrDefault();
        var secondaryPath = realPaths.Skip(1).FirstOrDefault();

        return new MappedRuneSelection(
            primaryPath?.PathId ?? 0,
            secondaryPath?.PathId ?? 0,
            primaryPath?.Runes ?? [],
            secondaryPath?.Runes ?? [],
            statShardsFallback);
    }

    private static List<int> SelectTree(
        IEnumerable<StoredRuneSelection> selections,
        RuneSelectionTree tree) => selections
        .Where(selection => selection.SelectionTree == tree)
        .OrderBy(selection => selection.SelectionIndex)
        .Select(selection => selection.RuneId)
        .ToList();

    private static int SelectStyle(
        IEnumerable<StoredRuneSelection> selections,
        RuneSelectionTree tree) => selections
        .Where(selection => selection.SelectionTree == tree && selection.StyleId > 0)
        .Select(selection => selection.StyleId)
        .FirstOrDefault();

    private static int ResolveRealPath(
        int runeId,
        Func<int, RuneSelectionMetadata?> resolveMetadata)
    {
        var metadata = resolveMetadata(runeId);
        return metadata.HasValue && RunePathIds.IsRealRunePath(metadata.Value.PathId)
            ? metadata.Value.PathId
            : 0;
    }

    private static bool HasStructuredSelections(IReadOnlyList<StoredRuneSelection> selections)
    {
        var hasNonDefaultHierarchy = selections.Any(selection =>
            selection.SelectionTree != RuneSelectionTree.Primary || selection.StyleId != 0);

        if (!hasNonDefaultHierarchy)
            return false;

        return selections
                   .Select(selection => (selection.SelectionTree, selection.SelectionIndex))
                   .Distinct()
                   .Count() == selections.Count;
    }
}
