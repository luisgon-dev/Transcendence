namespace Transcendence.Service.Core.Services.Analytics.Interfaces;

/// <summary>
/// Recomputes the precomputed analytics aggregate tables (<c>ChampionRoleTierStat</c>,
/// <c>ScopeMatchCountStat</c>, <c>ChampionBanScopeStat</c>) for a patch from the raw match data. The heavy
/// GROUP BY/distinct-match work that the analytics read surface used to do per request now runs here, on a
/// cadence, off the request path; reads roll these atoms up by scope as fast indexed lookups.
/// </summary>
public interface IPrecomputedAnalyticsRefresher
{
    /// <summary>
    /// Rebuilds the tabular-core aggregates (win-rate/pick-rate/role-rank source + ban-rate
    /// numerator/denominator) for <paramref name="patch"/>, replacing that patch's rows transactionally so
    /// reads never observe a partially-written patch.
    /// </summary>
    Task<PrecomputedAnalyticsRefreshResult> RefreshTabularCoreAsync(string patch, CancellationToken ct);

    /// <summary>
    /// Rebuilds the all-region lane-matchup aggregates (<c>ChampionMatchupStat</c>) for
    /// <paramref name="patch"/> — one big lane-pair self-join + timeline-diff aggregation grouped over every
    /// champion at once — replacing the patch's rows transactionally. Returns the row count written.
    /// </summary>
    Task<int> RefreshMatchupsAsync(string patch, CancellationToken ct);

    /// <summary>
    /// Recomputes + persists the durable build responses (<c>ChampionBuildSnapshot</c>) for
    /// <paramref name="patch"/>: the live <c>ComputeBuildsAsync</c> for each played (champion, role) at the
    /// common rank scopes (all-region), serialized so cold reads are a lookup instead of a raw scan.
    /// Returns the snapshot count written.
    /// </summary>
    Task<int> RefreshBuildsAsync(string patch, CancellationToken ct);

    /// <summary>
    /// Recomputes + persists the durable pro-surface responses (<c>AnalyticsResponseSnapshot</c>) for
    /// <paramref name="patch"/>: pro-playrate per roster scope, and pro-builds for each pro-played
    /// (champion, role) x roster scope, all-region. Returns the snapshot count written.
    /// </summary>
    Task<int> RefreshProSurfacesAsync(string patch, CancellationToken ct);
}

/// <summary>Row counts written per table, for logging/observability.</summary>
public sealed record PrecomputedAnalyticsRefreshResult(
    int RoleTierRows,
    int ScopeMatchCountRows,
    int BanScopeRows,
    int GradeRows);
