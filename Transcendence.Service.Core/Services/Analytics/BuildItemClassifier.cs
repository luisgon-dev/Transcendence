using Transcendence.Data.Models.LoL.Match;

namespace Transcendence.Service.Core.Services.Analytics;

/// <summary>
/// Minimal item metadata (a projection of <c>ItemVersion</c>) used to decide whether an item
/// is a build-relevant completed item and which build category it belongs to. Shared by the
/// timeline ingestion job (purchase-path capture) and the analytics compute service so both
/// use one definition of "completed build item".
/// </summary>
public readonly record struct BuildItemMetadata(
    IReadOnlyList<int> BuildsFrom,
    IReadOnlyList<int> BuildsInto,
    IReadOnlyList<string> Tags,
    bool InStore,
    int PriceTotal);

/// <summary>
/// Single source of truth for classifying League items into build-relevant categories.
/// </summary>
public static class BuildItemClassifier
{
    /// <summary>Purchases at or before this many ms from game start are eligible to be "starting items".</summary>
    public const int DefaultStarterCutoffMs = 90_000;

    // Non-build-impact item classes that never appear in completed build recommendations.
    public static readonly HashSet<string> ExcludedBuildItemCategories = new(StringComparer.OrdinalIgnoreCase)
    {
        "Consumable",
        "Trinket",
        "Vision"
    };

    // Trinket/ward/vision tags that disqualify an item from the "starting items" set.
    private static readonly HashSet<string> NonStarterCategories = new(StringComparer.OrdinalIgnoreCase)
    {
        "Trinket",
        "Vision"
    };

    // Used only when item metadata coverage is incomplete for the active patch (compute fallback).
    public static readonly HashSet<int> LegacyExcludedBuildItems = new()
    {
        // Trinkets and wards
        3340, 3363, 3364, 2055,
        // Consumables
        2003, 2010, 2031, 2033, 2138, 2139, 2140,
        // Starter and component items
        1001, 1004, 1011, 1018, 1026, 1027, 1028, 1029, 1031, 1033, 1035, 1036, 1037, 1038, 1042, 1043,
        1052, 1053, 1054, 1055, 1056, 1057, 1058, 1082, 1083, 2420, 2421, 2422, 3024, 3052, 3070
    };

    /// <summary>
    /// A completed, build-impacting legendary item: purchasable, has a cost, terminal in the build
    /// graph (no further upgrade), assembled from components, and not a consumable/trinket/ward.
    /// </summary>
    public static bool IsCompletedBuildItem(BuildItemMetadata metadata)
    {
        if (!metadata.InStore)
            return false;

        if (metadata.PriceTotal <= 0)
            return false;

        if (metadata.BuildsInto.Count > 0)
            return false;

        if (metadata.BuildsFrom.Count == 0)
            return false;

        return metadata.Tags.All(tag => !ExcludedBuildItemCategories.Contains(tag));
    }

    /// <summary>
    /// A purchased boots upgrade (tier-2 or tier-3). The basic Boots of Speed has no components and
    /// is treated as a starter/component, not a boots choice.
    /// </summary>
    public static bool IsBoots(BuildItemMetadata metadata) =>
        metadata.BuildsFrom.Count > 0 &&
        metadata.Tags.Any(tag => string.Equals(tag, "Boots", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Classifies a single timeline purchase into a build-relevant category, or returns null
    /// (dropped at ingestion time). Order matters: boots and completed legendaries take precedence
    /// over the time-based starter rule. Opening-phase purchases (≤ <paramref name="starterCutoffMs"/>)
    /// that are not trinkets/wards form the starter set — including starter consumables like potions.
    /// Trinkets, wards, and mid-game (post-opening) components/consumables return null.
    /// </summary>
    public static BuildItemCategory? Classify(BuildItemMetadata metadata, int timestampMs, int starterCutoffMs)
    {
        if (IsBoots(metadata))
            return BuildItemCategory.Boots;

        if (IsCompletedBuildItem(metadata))
            return BuildItemCategory.Legendary;

        if (timestampMs <= starterCutoffMs && metadata.InStore && IsStarterEligible(metadata))
            return BuildItemCategory.Starter;

        return null;
    }

    private static bool IsStarterEligible(BuildItemMetadata metadata) =>
        metadata.Tags.All(tag => !NonStarterCategories.Contains(tag));
}
