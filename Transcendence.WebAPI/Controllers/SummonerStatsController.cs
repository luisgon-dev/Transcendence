using Microsoft.AspNetCore.Mvc;
using Transcendence.Service.Core.Services.Analysis.Interfaces;
using Transcendence.Service.Core.Services.RiotApi.DTOs;
using Transcendence.WebAPI.Models.Stats;

namespace Transcendence.WebAPI.Controllers;

[ApiController]
[Route("api/summoners/{summonerId:guid}")]
public class SummonerStatsController(
    ISummonerStatsService statsService,
    ILogger<SummonerStatsController> logger) : ControllerBase
{
    /// <summary>
    ///     Gets overall statistics for a summoner.
    /// </summary>
    [HttpGet("stats/overview")]
    [ProducesResponseType(typeof(SummonerOverviewDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetOverview([FromRoute] Guid summonerId, [FromQuery] int recent = 20,
        CancellationToken ct = default)
    {
        try
        {
            var result = await statsService.GetSummonerOverviewAsync(summonerId, recent, ct);
            var dto = new SummonerOverviewDto(
                result.SummonerId,
                result.TotalMatches,
                result.Wins,
                result.Losses,
                result.WinRate,
                result.AvgKills,
                result.AvgDeaths,
                result.AvgAssists,
                result.KdaRatio,
                result.AvgCsPerMin,
                result.AvgVisionScore,
                result.AvgDamageToChamps,
                result.AvgGameDurationMin,
                result.RecentPerformance.Select(p => new RecentPerformanceDto(p.MatchId, p.Win, p.Kills, p.Deaths,
                    p.Assists, p.CsPerMin, p.VisionScore, p.DamageToChamps)).ToList()
            );
            return Ok(dto);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Request {RequestId}: failed to load overview stats for summoner {SummonerId}. Returning empty payload.",
                HttpContext.TraceIdentifier,
                summonerId);
            return Ok(new SummonerOverviewDto(
                summonerId,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                []));
        }
    }

    /// <summary>
    ///     Gets top champion stats for a summoner.
    /// </summary>
    [HttpGet("stats/champions")]
    [ProducesResponseType(typeof(List<ChampionStatDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetChampionStats([FromRoute] Guid summonerId, [FromQuery] int top = 10,
        CancellationToken ct = default)
    {
        try
        {
            var result = await statsService.GetChampionStatsAsync(summonerId, top, ct);
            var dto = result.Select(x => new ChampionStatDto(
                x.ChampionId,
                x.Games,
                x.Wins,
                x.Losses,
                x.WinRate,
                x.AvgKills,
                x.AvgDeaths,
                x.AvgAssists,
                x.KdaRatio,
                x.AvgCsPerMin,
                x.AvgVisionScore,
                x.AvgDamageToChamps
            )).ToList();
            return Ok(dto);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Request {RequestId}: failed to load champion stats for summoner {SummonerId}. Returning empty payload.",
                HttpContext.TraceIdentifier,
                summonerId);
            return Ok(new List<ChampionStatDto>());
        }
    }

    /// <summary>
    ///     Gets role breakdown for a summoner.
    /// </summary>
    [HttpGet("stats/roles")]
    [ProducesResponseType(typeof(List<RoleStatDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRoleBreakdown([FromRoute] Guid summonerId, CancellationToken ct = default)
    {
        try
        {
            var result = await statsService.GetRoleBreakdownAsync(summonerId, ct);
            var dto = result.Select(r => new RoleStatDto(r.Role, r.Games, r.Wins, r.Losses, r.WinRate)).ToList();
            return Ok(dto);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Request {RequestId}: failed to load role stats for summoner {SummonerId}. Returning empty payload.",
                HttpContext.TraceIdentifier,
                summonerId);
            return Ok(new List<RoleStatDto>());
        }
    }

    /// <summary>
    ///     Gets recent matches for a summoner with pagination.
    /// </summary>
    [HttpGet("matches/recent")]
    [ProducesResponseType(typeof(PagedResultDto<RecentMatchSummaryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRecentMatches([FromRoute] Guid summonerId, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        try
        {
            var result = await statsService.GetRecentMatchesAsync(summonerId, page, pageSize, ct);
            var dto = new PagedResultDto<RecentMatchSummaryDto>(
                result.Items.Select(m => new RecentMatchSummaryDto(
                    m.MatchId,
                    m.MatchDate,
                    m.DurationSeconds,
                    m.QueueType,
                    m.Win,
                    m.ChampionId,
                    m.TeamPosition,
                    m.Kills,
                    m.Deaths,
                    m.Assists,
                    m.VisionScore,
                    m.DamageToChamps,
                    m.CsPerMin,
                    m.SummonerSpell1Id,
                    m.SummonerSpell2Id,
                    m.Items,
                    new MatchRuneSummaryDto(m.Runes.PrimaryStyleId, m.Runes.SubStyleId, m.Runes.KeystoneId)
                )).ToList(),
                result.Page,
                result.PageSize,
                result.TotalCount,
                result.TotalPages
            );
            return Ok(dto);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Request {RequestId}: failed to load recent matches for summoner {SummonerId}. Returning empty payload.",
                HttpContext.TraceIdentifier,
                summonerId);

            var normalizedPage = page <= 0 ? 1 : page;
            var normalizedPageSize = pageSize <= 0 || pageSize > 100 ? 20 : pageSize;
            return Ok(new PagedResultDto<RecentMatchSummaryDto>([], normalizedPage, normalizedPageSize, 0, 0));
        }
    }

    /// <summary>
    ///     Gets full match details including all participants with items, runes, and spells.
    /// </summary>
    /// <param name="summonerId">The summoner ID (provides RESTful context for the match)</param>
    /// <param name="matchId">The Riot match ID (e.g., "NA1_1234567890")</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Full match details with all 10 participants</returns>
    [HttpGet("matches/{matchId}")]
    [ProducesResponseType(typeof(MatchDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMatchDetail(
        [FromRoute] Guid summonerId,
        [FromRoute] string matchId,
        CancellationToken ct = default)
    {
        try
        {
            var result = await statsService.GetMatchDetailAsync(matchId, ct);
            if (result == null)
                return NotFound();

            return Ok(result);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Request {RequestId}: failed to load match detail {MatchId} for summoner {SummonerId}. Returning 404 fallback.",
                HttpContext.TraceIdentifier,
                matchId,
                summonerId);
            return NotFound();
        }
    }
}
