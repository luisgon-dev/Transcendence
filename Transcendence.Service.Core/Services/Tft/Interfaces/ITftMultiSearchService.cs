using Transcendence.Service.Core.Services.Tft.Models;

namespace Transcendence.Service.Core.Services.Tft.Interfaces;

/// <summary>
/// Service for TFT multi-search (lobby scouting) functionality.
/// </summary>
public interface ITftMultiSearchService
{
    /// <summary>
    /// Searches for multiple TFT summoners by their Riot IDs.
    /// </summary>
    /// <param name="platformRegion">The platform region (e.g., NA1, EUW1).</param>
    /// <param name="riotIds">List of (gameName, tagLine) tuples to search for.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Multi-search results with lobby analysis.</returns>
    Task<TftMultiSearchResult> SearchAsync(
        string platformRegion,
        IReadOnlyList<(string GameName, string TagLine)> riotIds,
        CancellationToken ct = default);
}
