using Transcendence.Data.Models.LoL.Analytics;
using Transcendence.Service.Core.Services.Analytics.Models;

namespace Transcendence.Service.Core.Services.Analytics.Interfaces;

public interface IBuildResourceAnalyticsService
{
    Task<BuildResourceAnalyticsIndexResponse> GetItemsAsync(
        string? region,
        string? patch,
        CancellationToken ct = default);

    Task<BuildResourceAnalyticsDetailResponse?> GetItemAsync(
        int itemId,
        string? region,
        string? patch,
        CancellationToken ct = default);

    Task<BuildResourceAnalyticsIndexResponse> GetRunesAsync(
        string? region,
        string? patch,
        CancellationToken ct = default);

    Task<BuildResourceAnalyticsDetailResponse?> GetRuneAsync(
        int runeId,
        string? region,
        string? patch,
        CancellationToken ct = default);

    /// <summary>
    /// Caches the all-region item/rune index and detail payloads for one generation version from the
    /// stat rows the refresher holds in memory. Called by the refresher after each committed run.
    /// </summary>
    Task WarmGenerationAsync(
        Guid snapshotId,
        string patch,
        int processedMatchCount,
        IReadOnlyCollection<BuildResourceStat> stats,
        IReadOnlyCollection<BuildResourcePopulationStat> populations,
        CancellationToken ct = default);
}
