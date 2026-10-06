using Transcendence.Service.Core.Services.Analytics.Models;

namespace Transcendence.Service.Core.Services.Analytics.Interfaces;

/// <summary>
/// Read side of the public dataset stats. Serves the snapshot the worker last stored; it never computes
/// the stats itself, so a request costs at most one point lookup.
/// </summary>
public interface IDatasetStatsService
{
    /// <summary>The latest stored snapshot, or null when the worker has not computed one yet.</summary>
    Task<DatasetStatsDto?> GetAsync(CancellationToken ct = default);
}
