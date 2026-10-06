using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Transcendence.Data;
using Transcendence.Data.Models.LoL.Account;
using Transcendence.Data.Models.LoL.Analytics;
using Transcendence.Data.Models.LoL.Match;
using Transcendence.Service.Core.Services.Analytics.Interfaces;
using Transcendence.Service.Core.Services.Analytics.Models;
using Transcendence.Service.Core.Services.Jobs.Configuration;

namespace Transcendence.Service.Core.Services.Analytics.Implementations;

/// <summary>
/// Computes the public dataset stats and stores them as one <see cref="AnalyticsResponseSnapshot"/> row.
/// <para>
/// Cost, by design: every match figure comes from ONE grouped pass over <c>Matches</c> (~105 MB, kept hot
/// in shared buffers by the ingestion jobs that scan it all day; measured at ~1.2 s on production). The
/// players figure is PostgreSQL's planner estimate rather than a <c>count(*)</c>, because an exact count
/// of the ~4.8M-row <c>Summoners</c> table outlasts a 60 s statement timeout on the production disk. The
/// database size is a catalog call. Nothing here runs on a request; the WebAPI only reads the stored row.
/// </para>
/// </summary>
public sealed class DatasetStatsRefresher(
    TranscendenceContext db,
    IOptions<MultiRegionIngestionOptions> regionOptions) : IDatasetStatsRefresher
{
    private const int DailyAverageWindowDays = 7;

    public async Task<DatasetStatsDto> RefreshAsync(DateTime asOfUtc, CancellationToken ct = default)
    {
        var stats = await ComputeAsync(asOfUtc, ct);
        await StoreAsync(stats, ct);
        return stats;
    }

    internal async Task<DatasetStatsDto> ComputeAsync(DateTime asOfUtc, CancellationToken ct)
    {
        var now = asOfUtc.Kind == DateTimeKind.Utc ? asOfUtc : asOfUtc.ToUniversalTime();
        var since24Hours = now.AddHours(-24);
        // Whole UTC days only: today is still filling, so including it would drag the average down.
        var todayStartUtc = now.Date;
        var windowStartUtc = todayStartUtc.AddDays(-DailyAverageWindowDays);

        var activePatch = await db.Patches
            .AsNoTracking()
            .Where(p => p.IsActive)
            .Select(p => p.Version)
            .FirstOrDefaultAsync(ct);

        // One grouped pass; every figure is a filtered aggregate over the same rows.
        var perPlatform = await db.Matches
            .AsNoTracking()
            .Where(m => m.Status == FetchStatus.Success)
            .GroupBy(m => m.PlatformRegion)
            .Select(g => new PlatformAggregate(
                g.Key,
                g.LongCount(),
                g.LongCount(m => m.FetchedAt >= since24Hours),
                g.LongCount(m => m.FetchedAt >= windowStartUtc && m.FetchedAt < todayStartUtc),
                g.LongCount(m => activePatch != null && m.Patch == activePatch),
                g.Max(m => m.FetchedAt)))
            .ToListAsync(ct);

        var platforms = perPlatform
            .Where(row => !string.IsNullOrWhiteSpace(row.Platform))
            .Select(row => new DatasetPlatformStatsDto(
                row.Platform!,
                AnalyticsRegionCatalog.LabelFor(row.Platform!),
                row.Stored,
                row.Last24Hours))
            .OrderByDescending(row => row.MatchesStored)
            .ThenBy(row => row.Platform, StringComparer.Ordinal)
            .ToList();

        var crawledPlatforms = regionOptions.Value.Regions
            .Where(region => region.Enabled && !string.IsNullOrWhiteSpace(region.Region))
            .Select(region => region.Region.Trim().ToUpperInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var lastIngested = perPlatform.Max(row => row.LastFetchedAt);

        return new DatasetStatsDto(
            MatchesStored: perPlatform.Sum(row => row.Stored),
            MatchesLast24Hours: perPlatform.Sum(row => row.Last24Hours),
            MatchesPerDayLast7Days: (long)Math.Round(
                perPlatform.Sum(row => row.InDailyAverageWindow) / (double)DailyAverageWindowDays,
                MidpointRounding.AwayFromZero),
            ActivePatch: activePatch,
            ActivePatchMatches: perPlatform.Sum(row => row.OnActivePatch),
            PlayersIndexedEstimate: await EstimateRowCountAsync<Summoner>(ct),
            DatabaseSizeBytes: await DatabaseSizeAsync(ct),
            CrawledPlatforms: crawledPlatforms,
            Platforms: platforms,
            LastMatchIngestedAtUtc: lastIngested is { } last ? DateTime.SpecifyKind(last, DateTimeKind.Utc) : null,
            ComputedAtUtc: now);
    }

    private async Task StoreAsync(DatasetStatsDto stats, CancellationToken ct)
    {
        var payload = AnalyticsSnapshotSerialization.Serialize(stats);
        var updated = await db.AnalyticsResponseSnapshots
            .Where(x => x.Feature == AnalyticsSnapshotSerialization.DatasetStatsFeature &&
                        x.ScopeKey == AnalyticsSnapshotSerialization.DatasetStatsScopeKey &&
                        x.Patch == AnalyticsSnapshotSerialization.PatchIndependent)
            .ExecuteUpdateAsync(set => set
                .SetProperty(x => x.Payload, payload)
                .SetProperty(x => x.ComputedAtUtc, stats.ComputedAtUtc), ct);
        if (updated > 0)
            return;

        db.AnalyticsResponseSnapshots.Add(new AnalyticsResponseSnapshot
        {
            Id = Guid.NewGuid(),
            Feature = AnalyticsSnapshotSerialization.DatasetStatsFeature,
            ScopeKey = AnalyticsSnapshotSerialization.DatasetStatsScopeKey,
            Patch = AnalyticsSnapshotSerialization.PatchIndependent,
            Payload = payload,
            ComputedAtUtc = stats.ComputedAtUtc
        });
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// The planner's row estimate for an entity's table: a catalog read, refreshed by autovacuum/ANALYZE.
    /// Null for a never-analyzed table (<c>reltuples = -1</c>) rather than falling back to an exact count,
    /// which is exactly the scan this avoids.
    /// </summary>
    private async Task<long?> EstimateRowCountAsync<TEntity>(CancellationToken ct)
    {
        if (!db.Database.IsNpgsql())
            return null;

        var entityType = db.Model.FindEntityType(typeof(TEntity));
        var table = entityType?.GetTableName();
        if (table is null)
            return null;

        var schema = entityType!.GetSchema();
        var qualifiedName = schema is null ? Quote(table) : $"{Quote(schema)}.{Quote(table)}";
        var estimates = await db.Database
            .SqlQuery<long>($"SELECT reltuples::bigint AS \"Value\" FROM pg_class WHERE oid = to_regclass({qualifiedName})")
            .ToListAsync(ct);

        return estimates is [var estimate] && estimate >= 0 ? estimate : null;
    }

    private async Task<long?> DatabaseSizeAsync(CancellationToken ct)
    {
        if (!db.Database.IsNpgsql())
            return null;

        var sizes = await db.Database
            .SqlQuery<long>($"SELECT pg_database_size(current_database()) AS \"Value\"")
            .ToListAsync(ct);
        return sizes is [var size] ? size : null;
    }

    private static string Quote(string identifier) => $"\"{identifier.Replace("\"", "\"\"")}\"";

    private sealed record PlatformAggregate(
        string? Platform,
        long Stored,
        long Last24Hours,
        long InDailyAverageWindow,
        long OnActivePatch,
        DateTime? LastFetchedAt);
}
