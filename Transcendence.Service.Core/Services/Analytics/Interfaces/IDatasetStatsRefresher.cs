using Transcendence.Service.Core.Services.Analytics.Models;

namespace Transcendence.Service.Core.Services.Analytics.Interfaces;

/// <summary>
/// Worker side of the public dataset stats: computes them with one scan of <c>Matches</c> plus catalog
/// lookups, and stores the result as the snapshot <see cref="IDatasetStatsService"/> serves. Registered
/// only in the worker host, so the API cannot run the scan on a request.
/// </summary>
public interface IDatasetStatsRefresher
{
    Task<DatasetStatsDto> RefreshAsync(DateTime asOfUtc, CancellationToken ct = default);
}
