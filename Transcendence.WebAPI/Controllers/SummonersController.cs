using Camille.Enums;
using Hangfire;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Claims;
using Transcendence.Data.Repositories.Interfaces;
using Transcendence.Service.Core.Services.Analysis.Interfaces;
using Transcendence.Service.Core.Services.Diagnostics;
using Transcendence.Service.Core.Services.Jobs;
using Transcendence.Service.Core.Services.Jobs.Interfaces;
using Transcendence.Service.Core.Services.RiotApi;
using Transcendence.Service.Core.Services.RiotApi.DTOs;
using Transcendence.WebAPI.Models.MultiSearch;
using Transcendence.WebAPI.Security;

namespace Transcendence.WebAPI.Controllers;

[ApiController]
[Route("api/lol/summoners")]
[ProducesResponseType(StatusCodes.Status429TooManyRequests)]
public class SummonersController(
    ISummonerRepository summonerRepository,
    IRefreshLockRepository refreshLockRepository,
    IBackgroundJobClient backgroundJobClient,
    ISummonerStatsService statsService,
    IMultiSearchService multiSearchService,
    ILogger<SummonersController> logger
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
        var candidates = await summonerRepository.SearchByPrefixAsync(
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
    ///     This endpoint reads from the database only. If the summoner is not found, a background refresh will be required.
    /// </summary>
    /// <param name="region">
    ///     Platform route like NA1, EUW1, EUN1, KR, BR1, LA1, LA2, OC1, JP1, TR1, RU. Common short forms (na,
    ///     euw, eune, kr, br, lan, las, oce, jp, tr, ru) are also accepted.
    /// </param>
    /// <param name="name">Riot game name (without #tag)</param>
    /// <param name="tag">Riot tag (without #)</param>
    [HttpGet("{region}/{name}/{tag}")]
    [EnableRateLimiting("expensive-read")]
    [ProducesResponseType(typeof(SummonerProfileResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(SummonerAcceptedResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetByRiotId([FromRoute] string region, [FromRoute] string name,
        [FromRoute] string tag, CancellationToken ct)
    {
        if (!PlatformRouteParser.TryParse(region, out var platform))
            return BadRequest($"Unsupported region '{region}'. Use a platform like NA1, EUW1, EUN1, KR, etc.");

        var platformRegion = platform.ToString();
        var summoner = await summonerRepository.FindByRiotIdAsync(platformRegion, name, tag,
            q => q.Include(s => s.Ranks).Include(s => s.HistoricalRanks).AsSplitQuery(), ct);
        if (summoner != null)
        {
            // Map to response DTO with data age metadata
            var soloRank = summoner.Ranks
                .Where(r => IsSoloRankQueue(r.QueueType))
                .OrderByDescending(r => r.UpdatedAt)
                .FirstOrDefault();
            var flexRank = summoner.Ranks
                .Where(r => IsFlexRankQueue(r.QueueType))
                .OrderByDescending(r => r.UpdatedAt)
                .FirstOrDefault();

            // Keep these sequential because statsService shares a scoped DbContext, which is not thread-safe.
            var activeSeasonStats = await statsService.GetActiveSeasonProfileStatsAsync(summoner.Id, 5, 20, ct);
            var overview = activeSeasonStats.Overview;
            var champions = activeSeasonStats.Champions;
            var recent = await statsService.GetRecentMatchesAsync(summoner.Id, 1, 10, null, null, ct);
            var playedWith = await statsService.GetPlayedWithAsync(summoner.Id, 100, 6, ct);
            var mastery = await statsService.GetTopMasteryAsync(summoner.Id, 6, ct);

            // Calculate StatsAge from most recent match
            var mostRecentMatchDate = recent.Items.Count > 0 ? recent.Items[0].MatchDate : (long?)null;

            var response = new SummonerProfileResponse
            {
                SummonerId = summoner.Id,
                Puuid = summoner.Puuid ?? string.Empty,
                GameName = summoner.GameName ?? string.Empty,
                TagLine = summoner.TagLine ?? string.Empty,
                SummonerLevel = (int)summoner.SummonerLevel,
                ProfileIconId = summoner.ProfileIconId,

                SoloRank = soloRank != null ? new RankInfo
                {
                    Tier = soloRank.Tier,
                    Division = soloRank.RankNumber,
                    LeaguePoints = soloRank.LeaguePoints,
                    Wins = soloRank.Wins,
                    Losses = soloRank.Losses
                } : null,

                FlexRank = flexRank != null ? new RankInfo
                {
                    Tier = flexRank.Tier,
                    Division = flexRank.RankNumber,
                    LeaguePoints = flexRank.LeaguePoints,
                    Wins = flexRank.Wins,
                    Losses = flexRank.Losses
                } : null,

                // Overview stats
                OverviewStats = overview.TotalMatches > 0 ? new ProfileOverviewStats
                {
                    TotalMatches = overview.TotalMatches,
                    Wins = overview.Wins,
                    Losses = overview.Losses,
                    WinRate = overview.WinRate,
                    AvgKills = overview.AvgKills,
                    AvgDeaths = overview.AvgDeaths,
                    AvgAssists = overview.AvgAssists,
                    KdaRatio = overview.KdaRatio,
                    AvgCsPerMin = overview.AvgCsPerMin,
                    AvgVisionScore = overview.AvgVisionScore,
                    AvgDamageToChamps = overview.AvgDamageToChamps
                } : null,

                // Top 5 champions
                TopChampions = champions.Select(c => new ProfileChampionStat
                {
                    ChampionId = c.ChampionId,
                    ChampionName = ResolveChampionName(c.ChampionId),
                    Games = c.Games,
                    Wins = c.Wins,
                    Losses = c.Losses,
                    WinRate = c.WinRate,
                    KdaRatio = c.KdaRatio
                }).ToList(),

                ActiveSeason = new ProfileSeasonMetadata
                {
                    SeasonKey = activeSeasonStats.SeasonKey,
                    DisplayName = activeSeasonStats.SeasonDisplayName,
                    QueueScope = activeSeasonStats.QueueScope
                },

                FullHistory = activeSeasonStats.FullHistory != null ? new ProfileFullHistoryStatus
                {
                    Status = activeSeasonStats.FullHistory.Status,
                    RequestedAtUtc = activeSeasonStats.FullHistory.RequestedAtUtc,
                    StartedAtUtc = activeSeasonStats.FullHistory.StartedAtUtc,
                    CompletedAtUtc = activeSeasonStats.FullHistory.CompletedAtUtc,
                    UpdatedAtUtc = activeSeasonStats.FullHistory.UpdatedAtUtc,
                    PagesScanned = activeSeasonStats.FullHistory.PagesScanned,
                    MatchIdsDiscovered = activeSeasonStats.FullHistory.MatchIdsDiscovered,
                    FactsPersisted = activeSeasonStats.FullHistory.FactsPersisted,
                    DetailFetchFailures = activeSeasonStats.FullHistory.DetailFetchFailures,
                    CompletedMatchCount = activeSeasonStats.FullHistory.CompletedMatchCount,
                    RiotWins = activeSeasonStats.FullHistory.RiotWins,
                    RiotLosses = activeSeasonStats.FullHistory.RiotLosses,
                    RiotTotal = activeSeasonStats.FullHistory.RiotTotal,
                    RankedCountDelta = activeSeasonStats.FullHistory.RankedCountDelta,
                    CoverageStatus = activeSeasonStats.FullHistory.CoverageStatus,
                    ClassifierVersion = activeSeasonStats.FullHistory.ClassifierVersion
                } : null,

                FrequentlyPlayedWith = playedWith.Select(p => new FrequentlyPlayedWithStat
                {
                    SummonerId = p.SummonerId,
                    GameName = p.GameName ?? string.Empty,
                    TagLine = p.TagLine ?? string.Empty,
                    GamesTogether = p.GamesTogether,
                    SameTeamGames = p.SameTeamGames,
                    SameTeamWins = p.SameTeamWins
                }).ToList(),

                TopMastery = mastery.Select(m => new ChampionMasteryStat
                {
                    ChampionId = m.ChampionId,
                    ChampionName = ResolveChampionName(m.ChampionId),
                    ChampionLevel = m.ChampionLevel,
                    ChampionPoints = m.ChampionPoints,
                    LastPlayTime = m.LastPlayTime,
                    ChestGranted = m.ChestGranted,
                    TokensEarned = m.TokensEarned
                }).ToList(),

                ProfileAge = new DataAgeMetadata
                {
                    FetchedAt = summoner.UpdatedAt
                },

                RankAge = new DataAgeMetadata
                {
                    FetchedAt = soloRank?.UpdatedAt ?? flexRank?.UpdatedAt ?? DateTime.UtcNow
                },

                // Stats freshness based on most recent match
                StatsAge = mostRecentMatchDate.HasValue
                    ? new DataAgeMetadata
                    {
                        FetchedAt = DateTimeOffset.FromUnixTimeMilliseconds(mostRecentMatchDate.Value).UtcDateTime
                    }
                    : null
            };

            return Ok(response);
        }

        // If a refresh is in progress, let the caller know
        var refreshKey = RefreshLockKeys.BuildSummonerRefreshKey(platform, name, tag);
        var lockRow = await refreshLockRepository.GetAsync(refreshKey, ct);
        var pollUrl = Url.ActionLink(nameof(GetByRiotId), null, new
        {
            region,
            name,
            tag
        });
        if (lockRow != null && lockRow.LockedUntilUtc > DateTime.UtcNow)
        {
            var seconds = (int)(lockRow.LockedUntilUtc - DateTime.UtcNow).TotalSeconds;
            EmitTelemetry(() =>
            {
                var lockTelemetry = TryGetLockTelemetry();
                lockTelemetry?.RecordLifecycleOutcome(refreshKey, "contention", "summoners-controller");
                lockTelemetry?.RecordContentionWaitHint(refreshKey, Math.Max(1, seconds), "summoners-controller");
            });

            return Accepted(new SummonerAcceptedResponse(
                "Refresh in process",
                pollUrl,
                Math.Max(1, seconds)));
        }

        return Accepted(new SummonerAcceptedResponse(
            "Summoner not found in store. Use the refresh endpoint to queue a background refresh.",
            pollUrl));
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

        var key = RefreshLockKeys.BuildSummonerRefreshKey(platform, name, tag);
        var priorityKey = RefreshLockKeys.BuildApiPriorityKey(platform, name, tag);
        var ttl = TimeSpan.FromMinutes(15);
        var lockTelemetry = TryGetLockTelemetry();
        var pollUrl = Url.ActionLink(nameof(GetByRiotId), null, new
        {
            region,
            name,
            tag
        });

        var keyToken = await refreshLockRepository.TryAcquireOwnedAsync(key, ttl, ct);
        var acquired = keyToken is not null;
        if (!acquired)
        {
            var existing = await refreshLockRepository.GetAsync(key, ct);
            var seconds = existing == null
                ? (int)ttl.TotalSeconds
                : (int)Math.Max(1, (existing.LockedUntilUtc - DateTime.UtcNow).TotalSeconds);
            EmitTelemetry(() =>
            {
                lockTelemetry?.RecordLifecycleOutcome(key, "contention", "summoners-controller");
                lockTelemetry?.RecordContentionWaitHint(key, Math.Max(1, seconds), "summoners-controller");
            });

            logger.LogInformation(
                "[RefreshApi] LoL summoner refresh already in progress for {GameName}#{Tag} on {Platform}. retryAfterSeconds={RetryAfterSeconds}, traceId={TraceId}.",
                name,
                tag,
                platform,
                seconds,
                HttpContext.TraceIdentifier);

            return Accepted(new SummonerAcceptedResponse(
                "Refresh in process",
                pollUrl,
                seconds));
        }

        EmitTelemetry(() => lockTelemetry?.RecordLifecycleOutcome(key, "acquired", "summoners-controller"));

        var priorityToken = await refreshLockRepository.TryAcquireOwnedAsync(priorityKey, ttl, ct);
        var priorityAcquired = priorityToken is not null;
        if (!priorityAcquired)
        {
            EmitTelemetry(() =>
            {
                lockTelemetry?.RecordLifecycleOutcome(priorityKey, "contention", "summoners-controller");
                lockTelemetry?.RecordContentionWaitHint(
                    priorityKey,
                    (int)ttl.TotalSeconds,
                    "summoners-controller");
            });
        }

        // Enqueue refresh job
        try
        {
            backgroundJobClient.Enqueue<ISummonerRefreshJob>(job =>
                job.RefreshByRiotId(name, tag, platform,
                    RefreshLockKeys.BuildOwnedHandle(key, keyToken!.Value),
                    priorityAcquired ? RefreshLockKeys.BuildOwnedHandle(priorityKey, priorityToken!.Value) : null,
                    requestedByUserAccountId,
                    CancellationToken.None));
        }
        catch (Exception ex)
        {
            await refreshLockRepository.ReleaseOwnedAsync(key, keyToken!.Value, ct);
            if (priorityAcquired)
                await refreshLockRepository.ReleaseOwnedAsync(priorityKey, priorityToken!.Value, ct);

            logger.LogError(
                ex,
                "[RefreshApi] Failed to queue LoL summoner refresh for {GameName}#{Tag} on {Platform}. priorityLockAcquired={PriorityLockAcquired}, traceId={TraceId}.",
                name,
                tag,
                platform,
                priorityAcquired,
                HttpContext.TraceIdentifier);

            throw;
        }

        logger.LogInformation(
            "[RefreshApi] Queued LoL summoner refresh for {GameName}#{Tag} on {Platform}. priorityLockAcquired={PriorityLockAcquired}, traceId={TraceId}.",
            name,
            tag,
            platform,
            priorityAcquired,
            HttpContext.TraceIdentifier);

        return Accepted(new SummonerAcceptedResponse(
            "Refresh queued",
            pollUrl));
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
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
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

    /// <summary>
    /// Resolves champion ID to name. Phase 3 will add proper static data service.
    /// For now, returns placeholder that clients can resolve client-side.
    /// </summary>
    private static string ResolveChampionName(int championId)
    {
        return $"Champion {championId}";
    }

    private static bool IsSoloRankQueue(string? queueType)
    {
        if (string.IsNullOrWhiteSpace(queueType)) return false;
        return queueType.Equals("RANKED_SOLO_5x5", StringComparison.OrdinalIgnoreCase) ||
               queueType.Equals("RANKED_SOLO_5X5", StringComparison.OrdinalIgnoreCase) ||
               queueType.Equals("RANKED_SOLO_5V5", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsFlexRankQueue(string? queueType)
    {
        if (string.IsNullOrWhiteSpace(queueType)) return false;
        return queueType.StartsWith("RANKED_FLEX", StringComparison.OrdinalIgnoreCase);
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

    private IRefreshLockLifecycleTelemetry? TryGetLockTelemetry()
    {
        try
        {
            return HttpContext?.RequestServices.GetService<IRefreshLockLifecycleTelemetry>();
        }
        catch
        {
            return null;
        }
    }

    private bool TryGetUserId(out Guid userId)
    {
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(claim, out userId);
    }

    private static void EmitTelemetry(Action emit)
    {
        try
        {
            emit();
        }
        catch
        {
            // non-blocking telemetry only
        }
    }
}
