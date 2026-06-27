using Transcendence.Service.Core.Services.Analytics.Models;

namespace Transcendence.Service.Core.Services.Analytics.Interfaces;

/// <summary>
/// Raw computation service for champion matchups. Performs EF Core aggregation queries without caching.
/// The matchups-only contract left after the analytics god-file was decomposed (P10.1); win rates /
/// tier lists, builds, and the pro surfaces each have their own compute-service interface.
/// </summary>
public interface IChampionMatchupComputeService
{
    /// <summary>
    /// Computes matchup data (counters and favorable matchups) for a champion.
    /// Matchups are lane-specific (Mid vs Mid, Top vs Top, etc.).
    /// </summary>
    Task<ChampionMatchupsResponse> ComputeMatchupsAsync(
        int championId,
        string role,
        string? rankTier,
        string? region,
        string patch,
        CancellationToken ct);

    /// <summary>
    /// Matchups served from the precomputed <c>ChampionMatchupStat</c> aggregates (all-region scope), rolled
    /// up to the requested rank scope. Falls back to <see cref="ComputeMatchupsAsync"/> for a specific region
    /// (only the all-region scope is precomputed) or a patch without aggregates yet. Identical DTOs otherwise.
    /// </summary>
    Task<ChampionMatchupsResponse> ComputeMatchupsFromStatsAsync(
        int championId,
        string role,
        string? rankTier,
        string? region,
        string patch,
        CancellationToken ct);
}
