using Transcendence.Service.Core.Services.Analytics.Models;

namespace Transcendence.Service.Core.Services.Analytics.Interfaces;

/// <summary>
/// Raw + stats-backed computation for the pro / high-elo surfaces (pro builds, pro champion playrate, and
/// the public pro roster). Extracted from <see cref="IChampionAnalyticsComputeService"/> (P10.1) so this
/// domain is a focused unit; win rates / tier lists and builds live in their own services, and matchups
/// remain on the original. Performs EF Core aggregation without caching.
/// </summary>
public interface IChampionProComputeService
{
    /// <summary>
    /// Computes pro builds (recent matches, top players, common builds) for a champion among the tracked
    /// pro / high-elo roster. Scope selects the roster: "pro" (IsPro), "highelo" (IsHighEloOtp), or "all"
    /// (either).
    /// </summary>
    Task<ChampionProBuildsResponse> ComputeProBuildsAsync(
        int championId,
        string? region,
        string? role,
        string scope,
        string patch,
        CancellationToken ct);

    /// <summary>
    /// Computes champions ranked by pick/play frequency among tracked pro / high-elo players.
    /// Scope selects the roster: "pro" (IsPro), "highelo" (IsHighEloOtp), or "all" (either).
    /// </summary>
    Task<ProChampionPlayrateResponse> ComputeProChampionPlayrateAsync(
        string? region,
        string scope,
        string patch,
        CancellationToken ct);

    /// <summary>
    /// Pro builds served from the durable <c>AnalyticsResponseSnapshot</c> (the persisted live response),
    /// falling back to <see cref="ComputeProBuildsAsync"/> for a specific region, an all-roles request, or a
    /// patch without a snapshot. The stored value is the live compute's own output, so it's identical.
    /// </summary>
    Task<ChampionProBuildsResponse> ComputeProBuildsFromStatsAsync(
        int championId,
        string? region,
        string? role,
        string scope,
        string patch,
        CancellationToken ct);

    /// <summary>
    /// Pro champion playrate served from the durable snapshot, falling back to
    /// <see cref="ComputeProChampionPlayrateAsync"/> for a specific region or a patch without a snapshot.
    /// </summary>
    Task<ProChampionPlayrateResponse> ComputeProChampionPlayrateFromStatsAsync(
        string? region,
        string scope,
        string patch,
        CancellationToken ct);

    /// <summary>
    /// Returns the public roster of tracked pro players (IsActive &amp;&amp; IsPro).
    /// </summary>
    Task<List<ProPlayerDto>> ComputeProRosterAsync(
        string? region,
        CancellationToken ct);
}
