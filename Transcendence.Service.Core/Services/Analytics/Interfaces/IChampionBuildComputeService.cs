using Transcendence.Service.Core.Services.Analytics.Models;

namespace Transcendence.Service.Core.Services.Analytics.Interfaces;

/// <summary>
/// Raw computation service for champion builds (and their durable-snapshot fast path). Extracted from
/// <see cref="IChampionAnalyticsComputeService"/> so the builds domain is a focused unit; win rates /
/// tier lists, pro builds/playrate, and matchups remain elsewhere. Performs EF Core aggregation without
/// caching.
/// </summary>
public interface IChampionBuildComputeService
{
    /// <summary>
    /// Computes top 3 builds for a champion with items and runes bundled.
    /// Core items (70%+ appearance) distinguished from situational.
    /// Uses only completed, build-impact items for build quality calculations.
    /// </summary>
    Task<ChampionBuildsResponse> ComputeBuildsAsync(
        int championId,
        string role,
        string? rankTier,
        string? region,
        string patch,
        CancellationToken ct);

    /// <summary>
    /// Builds served from the durable <c>ChampionBuildSnapshot</c> (the persisted live response for the
    /// common all-region scopes), falling back to <see cref="ComputeBuildsAsync"/> for a specific tier/region
    /// or a patch without a snapshot yet. The stored value is the live compute's own output, so it's identical.
    /// </summary>
    Task<ChampionBuildsResponse> ComputeBuildsFromStatsAsync(
        int championId,
        string role,
        string? rankTier,
        string? region,
        string patch,
        CancellationToken ct);
}
