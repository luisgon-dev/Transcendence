namespace Transcendence.Service.Core.Services.Analytics.Models;

/// <summary>
/// The public scale-and-freshness summary of the stored match corpus: how many matches back the
/// analytics, how fast they arrive, and from where. The worker's <c>refresh-dataset-stats</c> job computes
/// it on a schedule and stores it as one <c>AnalyticsResponseSnapshot</c> row, so a read never scans
/// <c>Matches</c>.
/// </summary>
/// <param name="MatchesStored">Exact count of successfully fetched matches.</param>
/// <param name="MatchesLast24Hours">Matches fetched in the 24 hours before <paramref name="ComputedAtUtc"/>.</param>
/// <param name="MatchesPerDayLast7Days">
/// Matches fetched per day, averaged over the last 7 complete UTC days (today is excluded because it is
/// still filling).
/// </param>
/// <param name="ActivePatch">The active analytics patch, or null before the first patch is detected.</param>
/// <param name="ActivePatchMatches">Stored matches played on <paramref name="ActivePatch"/>, all queues.</param>
/// <param name="PlayersIndexedEstimate">
/// Approximate number of stored player (summoner) rows, read from PostgreSQL's planner statistics
/// (<c>pg_class.reltuples</c>) instead of a <c>count(*)</c> over millions of rows. Null when the
/// estimate is unavailable (a table that has never been analyzed, or a non-PostgreSQL provider).
/// </param>
/// <param name="DatabaseSizeBytes">On-disk size of the database, or null when unavailable.</param>
/// <param name="CrawledPlatforms">Platforms whose ranked ladders the worker crawls (enabled ingestion regions).</param>
/// <param name="Platforms">Stored matches per platform, largest first.</param>
/// <param name="LastMatchIngestedAtUtc">When the most recent match was fetched, as of <paramref name="ComputedAtUtc"/>.</param>
/// <param name="ComputedAtUtc">When the worker computed this snapshot.</param>
public sealed record DatasetStatsDto(
    long MatchesStored,
    long MatchesLast24Hours,
    long MatchesPerDayLast7Days,
    string? ActivePatch,
    long ActivePatchMatches,
    long? PlayersIndexedEstimate,
    long? DatabaseSizeBytes,
    IReadOnlyList<string> CrawledPlatforms,
    IReadOnlyList<DatasetPlatformStatsDto> Platforms,
    DateTime? LastMatchIngestedAtUtc,
    DateTime ComputedAtUtc);

/// <param name="Platform">Riot platform id (for example NA1, EUW1, KR).</param>
/// <param name="Label">Display name for the platform.</param>
/// <param name="MatchesStored">Successfully fetched matches played on this platform.</param>
/// <param name="MatchesLast24Hours">Of those, matches fetched in the last 24 hours.</param>
public sealed record DatasetPlatformStatsDto(
    string Platform,
    string Label,
    long MatchesStored,
    long MatchesLast24Hours);
