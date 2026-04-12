using Transcendence.Service.Core.Services.Tft.Models;

namespace Transcendence.Service.Core.Services.Tft.Interfaces;

/// <summary>
/// Service for computing TFT summoner statistics.
/// </summary>
public interface ITftSummonerStatsService
{
    /// <summary>
    /// Gets overall statistics for a TFT summoner.
    /// </summary>
    /// <param name="summonerId">The summoner ID.</param>
    /// <param name="recentGamesCount">Number of recent games to include.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Overview statistics.</returns>
    Task<TftSummonerOverviewStats> GetSummonerOverviewAsync(
        Guid summonerId, 
        int recentGamesCount, 
        CancellationToken ct);

    /// <summary>
    /// Gets top champion/unit stats for a TFT summoner.
    /// </summary>
    /// <param name="summonerId">The summoner ID.</param>
    /// <param name="top">Number of champions to return.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of champion statistics.</returns>
    Task<IReadOnlyList<TftChampionStat>> GetChampionStatsAsync(
        Guid summonerId, 
        int top, 
        CancellationToken ct);
}
