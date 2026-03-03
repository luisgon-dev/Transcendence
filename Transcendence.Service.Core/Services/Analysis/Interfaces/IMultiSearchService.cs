using Transcendence.Service.Core.Services.Analysis.Models;

namespace Transcendence.Service.Core.Services.Analysis.Interfaces;

public interface IMultiSearchService
{
    /// <summary>
    /// Batch lookup summoners by Riot ID and return per-summoner analysis with team-level insights.
    /// Designed for the champ select time window - only returns data already in the database (no background refresh).
    /// </summary>
    Task<MultiSearchResult> SearchAsync(
        string platformRegion,
        IReadOnlyList<(string GameName, string TagLine)> riotIds,
        CancellationToken ct = default);
}
