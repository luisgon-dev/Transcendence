using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Security.Claims;
using Transcendence.Service.Core.Services.Analysis.Interfaces;
using Transcendence.Service.Core.Services.Refresh.Interfaces;
using Transcendence.Service.Core.Services.RiotApi;
using Transcendence.Service.Core.Services.RiotApi.DTOs;
using Transcendence.Service.Core.Services.Summoners.Interfaces;
using Transcendence.WebAPI.Models.MultiSearch;
using Transcendence.WebAPI.Security;

namespace Transcendence.WebAPI.Controllers;

[ApiController]
[Route("api/lol/summoners")]
[ProducesResponseType(StatusCodes.Status429TooManyRequests)]
public class SummonersController(
    ISummonerProfileService summonerProfileService,
    ISummonerRefreshCoordinator refreshCoordinator,
    IMultiSearchService multiSearchService
) : ControllerBase
{
    /// <summary>
    ///     Search summoners already stored in the database for autosuggest.
    /// </summary>
    /// <param name="region">Platform route or alias (e.g., NA1 or na).</param>
    /// <param name="q">Search input. Supports "gameName" or "gameName#tag" prefix forms.</param>
    /// <param name="limit">Max number of items to return. Default 8, max 10.</param>
    [HttpGet("search")]
    [EnableRateLimiting("search-read")]
    [ProducesResponseType(typeof(SummonerSearchResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Search(
        [FromQuery] string region,
        [FromQuery] string q,
        [FromQuery] int? limit,
        CancellationToken ct)
    {
        if (!PlatformRouteParser.TryParse(region, out var platform))
            return BadRequest($"Unsupported region '{region}'. Use a platform like NA1, EUW1, EUN1, KR, etc.");

        if (!TryParseSearchQuery(q, out var gameNamePrefix, out var tagLinePrefix))
            return BadRequest("Query must be at least 2 characters and use at most one '#' delimiter.");

        var safeLimit = Math.Clamp(limit ?? 8, 1, 10);
        var candidates = await summonerProfileService.SearchByPrefixAsync(
            platform.ToString(),
            gameNamePrefix,
            tagLinePrefix,
            safeLimit,
            ct);

        var response = new SummonerSearchResponse
        {
            Items = candidates.Select(candidate => new SummonerSearchItem
            {
                PlatformRegion = candidate.PlatformRegion,
                Region = ToRegionSlug(candidate.PlatformRegion),
                GameName = candidate.GameName,
                TagLine = candidate.TagLine,
                ProfileIconId = candidate.ProfileIconId
            }).ToList()
        };

        Response.Headers.CacheControl = "public, max-age=15, stale-while-revalidate=30";
        return Ok(response);
    }

    /// <summary>
    ///     Get summoner information by Riot ID (gameName and tagLine) and platform region (e.g., NA1, EUW1).
    ///     This endpoint reads from the database only and always returns a typed lookup state. A missing state requires an
    ///     explicit signed-in refresh request; a refreshing state can be polled until it becomes ready.
    /// </summary>
    /// <param name="region">
    ///     Platform route like NA1, EUW1, EUN1, KR, BR1, LA1, LA2, OC1, JP1, TR1, RU. Common short forms (na,
    ///     euw, eune, kr, br, lan, las, oce, jp, tr, ru) are also accepted.
    /// </param>
    /// <param name="name">Riot game name (without #tag)</param>
    /// <param name="tag">Riot tag (without #)</param>
    [HttpGet("{region}/{name}/{tag}")]
    [EnableRateLimiting("expensive-read")]
    [ProducesResponseType(typeof(SummonerLookupResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetByRiotId([FromRoute] string region, [FromRoute] string name,
        [FromRoute] string tag, CancellationToken ct)
    {
        if (!PlatformRouteParser.TryParse(region, out var platform))
            return BadRequest($"Unsupported region '{region}'. Use a platform like NA1, EUW1, EUN1, KR, etc.");

        var profile = await summonerProfileService.GetProfileByRiotIdAsync(platform.ToString(), name, tag, ct);
        if (profile is not null)
            return Ok(new SummonerLookupResponse(SummonerLookupStatuses.Ready, Profile: profile));

        var pollUrl = Url.ActionLink(nameof(GetByRiotId), null, new
        {
            region,
            name,
            tag
        });
        var progress = await refreshCoordinator.GetProgressAsync(
            name,
            tag,
            platform,
            "summoners-controller",
            ct);
        if (progress is not null)
        {
            return Ok(new SummonerLookupResponse(
                SummonerLookupStatuses.Refreshing,
                Message: "Refresh in process",
                Poll: pollUrl,
                RetryAfterSeconds: progress.RetryAfterSeconds));
        }

        return Ok(new SummonerLookupResponse(
            SummonerLookupStatuses.Missing,
            Message: "Summoner not found in store. Use the refresh endpoint to queue a background refresh.",
            Poll: pollUrl));
    }

    /// <summary>
    ///     Queue a background refresh for the specified summoner by Riot ID. Only one refresh can be in-flight at a time.
    /// </summary>
    [HttpPost("{region}/{name}/{tag}/refresh")]
    [Authorize(Policy = AuthPolicies.UserOnly)]
    [EnableRateLimiting("expensive-read")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(
        typeof(SummonerAcceptedResponse),
        StatusCodes.Status202Accepted,
        Description =
            "Accepted. Returns \"Refresh queued\" when the refresh lock is acquired, or \"Refresh in process\" with retryAfterSeconds when contention is detected.")]
    public async Task<IActionResult> RefreshByRiotId([FromRoute] string region, [FromRoute] string name,
        [FromRoute] string tag, CancellationToken ct)
    {
        if (!PlatformRouteParser.TryParse(region, out var platform))
            return BadRequest($"Unsupported region '{region}'. Use a platform like NA1, EUW1, EUN1, KR, etc.");

        if (!TryGetUserId(out var requestedByUserAccountId))
            return Unauthorized();

        var pollUrl = Url.ActionLink(nameof(GetByRiotId), null, new
        {
            region,
            name,
            tag
        });
        var outcome = await refreshCoordinator.EnqueueRefreshAsync(
            name,
            tag,
            platform,
            pollUrl,
            HttpContext.TraceIdentifier,
            requestedByUserAccountId,
            "summoners-controller",
            ct);

        return Accepted(new SummonerAcceptedResponse(
            outcome.WasQueued ? "Refresh queued" : "Refresh in process",
            outcome.PollUrl,
            outcome.RetryAfterSeconds));
    }

    /// <summary>
    ///     Batch lookup multiple summoners for champ select analysis.
    ///     Returns per-summoner stats, ranks, top champions, role distribution, and team-level insights.
    ///     Only returns data already in the database (no background refresh).
    /// </summary>
    [HttpPost("multi-search")]
    [Authorize(Policy = AuthPolicies.AppOnly)]
    [EnableRateLimiting("multisearch-read")]
    [ProducesResponseType(typeof(MultiSearchResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> MultiSearch(
        [FromBody] MultiSearchRequest request,
        CancellationToken ct)
    {
        if (!PlatformRouteParser.TryParse(request.Region, out var platform))
            return BadRequest($"Unsupported region '{request.Region}'. Use a platform like NA1, EUW1, EUN1, KR, etc.");

        if (request.Summoners.Count == 0)
            return BadRequest("At least one summoner is required.");

        if (request.Summoners.Count > 5)
            return BadRequest("Maximum 5 summoners per request.");

        var riotIds = request.Summoners
            .Select(s => (s.GameName, s.TagLine))
            .ToList();

        var result = await multiSearchService.SearchAsync(platform.ToString(), riotIds, ct);

        // Map service models to response DTOs.
        var response = new MultiSearchResponse(
            Results: result.Summoners.Select(s => new MultiSearchSummonerResult(
                GameName: s.GameName,
                TagLine: s.TagLine,
                Found: s.Found,
                SummonerId: s.SummonerId,
                ProfileIconId: s.ProfileIconId,
                SummonerLevel: s.SummonerLevel,
                SoloRank: s.SoloRank != null
                    ? new MultiSearchRankInfo(s.SoloRank.Tier, s.SoloRank.Division, s.SoloRank.LeaguePoints,
                        s.SoloRank.Wins, s.SoloRank.Losses)
                    : null,
                FlexRank: s.FlexRank != null
                    ? new MultiSearchRankInfo(s.FlexRank.Tier, s.FlexRank.Division, s.FlexRank.LeaguePoints,
                        s.FlexRank.Wins, s.FlexRank.Losses)
                    : null,
                OverviewStats: s.Overview != null
                    ? new MultiSearchOverviewStats(
                        s.Overview.TotalMatches,
                        s.Overview.Wins,
                        s.Overview.Losses,
                        s.Overview.WinRate,
                        s.Overview.AvgKills,
                        s.Overview.AvgDeaths,
                        s.Overview.AvgAssists,
                        s.Overview.KdaRatio)
                    : null,
                TopChampions: s.TopChampions?.Select(c => new MultiSearchChampionStat(
                    c.ChampionId, c.Games, c.Wins, c.WinRate, c.KdaRatio)).ToList(),
                RoleDistribution: s.Roles != null && s.Overview is { TotalMatches: > 0 }
                    ? s.Roles.Select(r => new MultiSearchRoleStat(
                        r.Role, r.Games,
                        Math.Round((double)r.Games / s.Overview.TotalMatches * 100.0, 1))).ToList()
                    : null,
                PrimaryRole: s.PrimaryRole
            )).ToList(),
            TeamInsights: new MultiSearchTeamInsights(
                AverageRankScore: result.TeamAnalysis.AverageRankScore,
                AverageRankLabel: result.TeamAnalysis.AverageRankLabel,
                RoleCoverage: result.TeamAnalysis.RoleCoverage.ToList(),
                MissingRoles: result.TeamAnalysis.MissingRoles.ToList(),
                PotentialAutofills: result.TeamAnalysis.PotentialAutofills
                    .Select(a => new MultiSearchAutofillRisk(a.GameName, a.TagLine, a.PrimaryRole, a.Note))
                    .ToList()
            )
        );

        return Ok(response);
    }

    private static bool TryParseSearchQuery(string? rawQuery, out string gameNamePrefix, out string? tagLinePrefix)
    {
        gameNamePrefix = string.Empty;
        tagLinePrefix = null;

        if (string.IsNullOrWhiteSpace(rawQuery))
            return false;

        var query = rawQuery.Trim();
        if (query.Length < 2 || query.Length > 64)
            return false;

        var firstHashIdx = query.IndexOf('#');
        if (firstHashIdx < 0)
        {
            gameNamePrefix = query;
            return true;
        }

        if (query.IndexOf('#', firstHashIdx + 1) >= 0)
            return false;

        var gameName = query[..firstHashIdx].Trim();
        var tagLine = query[(firstHashIdx + 1)..].Trim();
        if (string.IsNullOrWhiteSpace(gameName))
            return false;

        gameNamePrefix = gameName;
        tagLinePrefix = string.IsNullOrWhiteSpace(tagLine) ? null : tagLine;
        return true;
    }

    private static string ToRegionSlug(string platformRegion)
    {
        return platformRegion.ToUpperInvariant() switch
        {
            "NA1" => "na",
            "EUW1" => "euw",
            "EUN1" => "eune",
            "KR" => "kr",
            "BR1" => "br",
            "LA1" => "lan",
            "LA2" => "las",
            "OC1" => "oce",
            "JP1" => "jp",
            "TR1" => "tr",
            "RU" => "ru",
            _ => platformRegion.ToLowerInvariant()
        };
    }

    private bool TryGetUserId(out Guid userId)
    {
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(claim, out userId);
    }
}
