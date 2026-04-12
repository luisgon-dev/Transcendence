using Camille.Enums;
using Hangfire;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Transcendence.Data.Repositories.Interfaces;
using Transcendence.Service.Core.Services.Diagnostics;
using Transcendence.Service.Core.Services.Jobs;
using Transcendence.Service.Core.Services.RiotApi;
using Transcendence.Service.Core.Services.RiotApi.DTOs;
using Transcendence.Service.Core.Services.Tft.Interfaces;
using Transcendence.WebAPI.Models;

namespace Transcendence.WebAPI.Controllers;

[ApiController]
[Route("api/tft/summoners")]
[ProducesResponseType(StatusCodes.Status429TooManyRequests)]
public class TftSummonersController(
    ITftSummonerReadService readService,
    ITftMultiSearchService multiSearchService,
    IRefreshLockRepository refreshLockRepository,
    IBackgroundJobClient backgroundJobClient,
    ILogger<TftSummonersController> logger) : ControllerBase
{
    [HttpGet("search")]
    [EnableRateLimiting("search-read")]
    public async Task<IActionResult> Search(
        [FromQuery] string region,
        [FromQuery] string q,
        [FromQuery] int? limit,
        CancellationToken ct)
    {
        if (!PlatformRouteParser.TryParse(region, out var platform))
            return BadRequest($"Unsupported region '{region}'.");

        if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 2)
            return BadRequest("Query must be at least 2 characters.");

        var results = await readService.SearchAsync(platform.ToString(), q, Math.Clamp(limit ?? 8, 1, 10), ct);
        return Ok(new TftSummonerSearchResponse(
            results.Select(x => new TftSummonerSearchItem(x.PlatformRegion, x.Region, x.GameName, x.TagLine, x.ProfileIconId)).ToList()));
    }

    [HttpGet("{region}/{name}/{tag}")]
    [EnableRateLimiting("expensive-read")]
    [ProducesResponseType(typeof(Transcendence.Service.Core.Services.Tft.Models.TftSummonerProfileDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(SummonerAcceptedResponse), StatusCodes.Status202Accepted)]
    public async Task<IActionResult> GetByRiotId([FromRoute] string region, [FromRoute] string name, [FromRoute] string tag,
        CancellationToken ct)
    {
        if (!PlatformRouteParser.TryParse(region, out var platform))
            return BadRequest($"Unsupported region '{region}'.");

        var profile = await readService.GetProfileByRiotIdAsync(platform.ToString(), name, tag, ct);
        if (profile != null)
            return Ok(profile);

        var lockKey = TftRefreshLockKeys.BuildSummonerRefreshKey(platform, name, tag);
        var lockRow = await refreshLockRepository.GetAsync(lockKey, ct);
        var pollUrl = Url.ActionLink(nameof(GetByRiotId), null, new { region, name, tag });
        if (lockRow != null && lockRow.LockedUntilUtc > DateTime.UtcNow)
        {
            var seconds = Math.Max(1, (int)(lockRow.LockedUntilUtc - DateTime.UtcNow).TotalSeconds);
            return Accepted(new SummonerAcceptedResponse("Refresh in process", pollUrl, seconds));
        }

        return Accepted(new SummonerAcceptedResponse(
            "Summoner not found in store. Use the refresh endpoint to queue a background refresh.",
            pollUrl,
            null));
    }

    [HttpPost("{region}/{name}/{tag}/refresh")]
    [EnableRateLimiting("expensive-read")]
    [ProducesResponseType(typeof(SummonerAcceptedResponse), StatusCodes.Status202Accepted)]
    public async Task<IActionResult> RefreshByRiotId([FromRoute] string region, [FromRoute] string name, [FromRoute] string tag,
        CancellationToken ct)
    {
        if (!PlatformRouteParser.TryParse(region, out var platform))
            return BadRequest($"Unsupported region '{region}'.");

        var key = TftRefreshLockKeys.BuildSummonerRefreshKey(platform, name, tag);
        var priorityKey = TftRefreshLockKeys.BuildApiPriorityKey(platform, name, tag);
        var ttl = TimeSpan.FromMinutes(15);
        var pollUrl = Url.ActionLink(nameof(GetByRiotId), null, new { region, name, tag });

        var acquired = await refreshLockRepository.TryAcquireAsync(key, ttl, ct);
        if (!acquired)
        {
            var existing = await refreshLockRepository.GetAsync(key, ct);
            var seconds = existing == null
                ? (int)ttl.TotalSeconds
                : Math.Max(1, (int)(existing.LockedUntilUtc - DateTime.UtcNow).TotalSeconds);

            logger.LogInformation(
                "[RefreshApi] TFT summoner refresh already in progress for {GameName}#{Tag} on {Platform}. retryAfterSeconds={RetryAfterSeconds}, traceId={TraceId}.",
                name,
                tag,
                platform,
                seconds,
                HttpContext.TraceIdentifier);

            return Accepted(new SummonerAcceptedResponse("Refresh in process", pollUrl, seconds));
        }

        var priorityAcquired = await refreshLockRepository.TryAcquireAsync(priorityKey, ttl, ct);
        try
        {
            backgroundJobClient.Enqueue<ITftSummonerRefreshJob>(job =>
                job.RefreshByRiotId(name, tag, platform, key, priorityAcquired ? priorityKey : null,
                    CancellationToken.None));
        }
        catch (Exception ex)
        {
            await refreshLockRepository.ReleaseAsync(key, ct);
            if (priorityAcquired)
                await refreshLockRepository.ReleaseAsync(priorityKey, ct);

            logger.LogError(
                ex,
                "[RefreshApi] Failed to queue TFT summoner refresh for {GameName}#{Tag} on {Platform}. priorityLockAcquired={PriorityLockAcquired}, traceId={TraceId}.",
                name,
                tag,
                platform,
                priorityAcquired,
                HttpContext.TraceIdentifier);

            throw;
        }

        logger.LogInformation(
            "[RefreshApi] Queued TFT summoner refresh for {GameName}#{Tag} on {Platform}. priorityLockAcquired={PriorityLockAcquired}, traceId={TraceId}.",
            name,
            tag,
            platform,
            priorityAcquired,
            HttpContext.TraceIdentifier);

        return Accepted(new SummonerAcceptedResponse("Refresh queued", pollUrl, null));
    }

    [HttpGet("{summonerId:guid}/matches/recent")]
    [EnableRateLimiting("expensive-read")]
    public async Task<IActionResult> GetRecentMatches([FromRoute] Guid summonerId, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        return Ok(await readService.GetRecentMatchesAsync(summonerId, page, pageSize, ct));
    }

    [HttpGet("{summonerId:guid}/matches/{matchId}")]
    [EnableRateLimiting("expensive-read")]
    public async Task<IActionResult> GetMatchDetail([FromRoute] Guid summonerId, [FromRoute] string matchId,
        CancellationToken ct = default)
    {
        var result = await readService.GetMatchDetailAsync(summonerId, matchId, ct);
        return result == null ? NotFound() : Ok(result);
    }

    [HttpPost("multi-search")]
    [EnableRateLimiting("multisearch-read")]
    [ProducesResponseType(typeof(TftMultiSearchResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> MultiSearch([FromBody] TftMultiSearchRequest request, CancellationToken ct = default)
    {
        if (!PlatformRouteParser.TryParse(request.Region, out var platform))
            return BadRequest($"Unsupported region '{request.Region}'.");

        if (request.RiotIds.Count == 0 || request.RiotIds.Count > 8)
            return BadRequest("Must provide between 1 and 8 Riot IDs.");

        // Parse riotIds (format: "name#tag")
        var parsedIds = request.RiotIds
            .Select(id =>
            {
                var parts = id.Split('#', 2);
                return parts.Length == 2 ? (parts[0], parts[1]) : (id, "");
            })
            .ToList();

        var result = await multiSearchService.SearchAsync(platform.ToString(), parsedIds, ct);

        var dto = new TftMultiSearchResponse(
            result.Summoners.Select(s => new TftMultiSearchSummonerDto(
                s.GameName,
                s.TagLine,
                s.Found,
                s.SummonerId,
                s.ProfileIconId,
                s.SummonerLevel,
                s.Rank != null ? new TftMultiSearchRankDto(
                    s.Rank.Tier,
                    s.Rank.Division,
                    s.Rank.LeaguePoints,
                    s.Rank.Wins,
                    s.Rank.Losses) : null,
                s.Overview != null ? new TftMultiSearchOverviewDto(
                    s.Overview.TotalMatches,
                    s.Overview.AveragePlacement,
                    s.Overview.Top4Rate,
                    s.Overview.WinRate) : null,
                s.TopUnits?.Select(u => new TftMultiSearchChampionDto(
                    u.CharacterId,
                    u.Name,
                    u.Cost,
                    u.GamesPlayed,
                    u.AveragePlacement,
                    u.Top4Rate,
                    u.WinRate)).ToList()
            )).ToList(),
            new TftMultiSearchLobbyAnalysisDto(
                result.LobbyAnalysis.AverageRankScore,
                result.LobbyAnalysis.AverageRankLabel,
                result.LobbyAnalysis.FoundPlayers,
                result.LobbyAnalysis.TotalPlayers,
                result.LobbyAnalysis.PotentialIssues.Select(i => new TftMultiSearchAutofillDto(
                    i.GameName,
                    i.TagLine,
                    i.Note)).ToList()
            )
        );

        return Ok(dto);
    }
}

public record TftSummonerSearchItem(
    string PlatformRegion,
    string Region,
    string GameName,
    string TagLine,
    int ProfileIconId
);

public record TftSummonerSearchResponse(
    IReadOnlyList<TftSummonerSearchItem> Items
);
