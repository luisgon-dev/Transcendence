using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Transcendence.Service.Core.Services.Tft.Interfaces;
using Transcendence.WebAPI.Models.Stats;

namespace Transcendence.WebAPI.Controllers;

/// <summary>
/// Controller for TFT summoner statistics endpoints.
/// </summary>
[ApiController]
[Route("api/tft/summoners/{summonerId:guid}")]
[EnableRateLimiting("expensive-read")]
[ProducesResponseType(StatusCodes.Status429TooManyRequests)]
public class TftSummonerStatsController(ITftSummonerStatsService statsService) : ControllerBase
{
    /// <summary>
    /// Gets overall statistics for a TFT summoner.
    /// </summary>
    /// <param name="summonerId">The summoner ID.</param>
    /// <param name="recent">Number of recent games to include (default 20, max 100).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Overview statistics including average placement, top4 rate, and win rate.</returns>
    [HttpGet("stats/overview")]
    [ProducesResponseType(typeof(TftSummonerOverviewDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetOverview(
        [FromRoute] Guid summonerId, 
        [FromQuery] int recent = 20, 
        CancellationToken ct = default)
    {
        var result = await statsService.GetSummonerOverviewAsync(summonerId, recent, ct);
        
        var dto = new TftSummonerOverviewDto(
            result.SummonerId,
            result.TotalMatches,
            result.AveragePlacement,
            result.Top4Rate,
            result.WinRate,
            result.RecentPerformance.Select(p => new TftRecentPerformanceDto(
                p.MatchId,
                p.MatchDate,
                p.Placement,
                p.Level,
                p.SetCoreName,
                p.Patch,
                p.Units.Select(u => new TftUnitPerformanceDto(
                    u.CharacterId,
                    u.Name,
                    u.Tier,
                    u.Items)).ToList(),
                p.Traits.Select(t => new TftTraitPerformanceDto(
                    t.Name,
                    t.NumUnits,
                    t.TierCurrent)).ToList()
            )).ToList()
        );
        
        return Ok(dto);
    }

    /// <summary>
    /// Gets top champion/unit stats for a TFT summoner.
    /// </summary>
    /// <param name="summonerId">The summoner ID.</param>
    /// <param name="top">Number of champions to return (default 10, max 50).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of champion statistics including games played, average placement, and win rates.</returns>
    [HttpGet("stats/champions")]
    [ProducesResponseType(typeof(List<TftChampionStatDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetChampionStats(
        [FromRoute] Guid summonerId, 
        [FromQuery] int top = 10, 
        CancellationToken ct = default)
    {
        var result = await statsService.GetChampionStatsAsync(summonerId, top, ct);
        
        var dto = result.Select(x => new TftChampionStatDto(
            x.CharacterId,
            x.Name,
            x.Cost,
            x.GamesPlayed,
            x.AveragePlacement,
            x.Top4Rate,
            x.WinRate
        )).ToList();
        
        return Ok(dto);
    }
}
