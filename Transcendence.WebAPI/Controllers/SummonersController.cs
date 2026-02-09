using Camille.Enums;
using Hangfire;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Transcendence.Data.Repositories.Interfaces;
using Transcendence.Service.Core.Services.Analysis.Interfaces;
using Transcendence.Service.Core.Services.Jobs.Interfaces;
using Transcendence.Service.Core.Services.RiotApi;
using Transcendence.Service.Core.Services.RiotApi.DTOs;

namespace Transcendence.WebAPI.Controllers;

[ApiController]
[Route("api/summoners")]
public class SummonersController(
    ISummonerRepository summonerRepository,
    IRefreshLockRepository refreshLockRepository,
    IBackgroundJobClient backgroundJobClient,
    ISummonerStatsService statsService
) : ControllerBase
{
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
    [ProducesResponseType(typeof(SummonerProfileResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(SummonerAcceptedResponse), StatusCodes.Status202Accepted)]
    public async Task<IActionResult> GetByRiotId([FromRoute] string region, [FromRoute] string name,
        [FromRoute] string tag, CancellationToken ct)
    {
        if (!PlatformRouteParser.TryParse(region, out var platform))
            return BadRequest($"Unsupported region '{region}'. Use a platform like NA1, EUW1, EUN1, KR, etc.");

        var platformRegion = platform.ToString();
        var summoner = await summonerRepository.FindByRiotIdAsync(platformRegion, name, tag,
            q => q.Include(s => s.Ranks).Include(s => s.HistoricalRanks), ct);
        if (summoner != null)
        {
            // Map to response DTO with data age metadata
            var soloRank = summoner.Ranks.FirstOrDefault(r => r.QueueType == "RANKED_SOLO_5x5");
            var flexRank = summoner.Ranks.FirstOrDefault(r => r.QueueType == "RANKED_FLEX_SR");

            // Keep these sequential because statsService shares a scoped DbContext, which is not thread-safe.
            var overview = await statsService.GetSummonerOverviewAsync(summoner.Id, 20, ct);
            var champions = await statsService.GetChampionStatsAsync(summoner.Id, 5, ct);
            var recent = await statsService.GetRecentMatchesAsync(summoner.Id, 1, 10, ct);

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

                // Recent 10 matches
                RecentMatches = recent.Items.Select(m => new ProfileRecentMatch
                {
                    MatchId = m.MatchId,
                    MatchDate = m.MatchDate,
                    QueueType = m.QueueType,
                    Win = m.Win,
                    ChampionId = m.ChampionId,
                    ChampionName = ResolveChampionName(m.ChampionId),
                    Kills = m.Kills,
                    Deaths = m.Deaths,
                    Assists = m.Assists,
                    CsPerMin = m.CsPerMin
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
        var refreshKey = BuildRefreshKey(platform, name, tag);
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
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(SummonerAcceptedResponse), StatusCodes.Status202Accepted)]
    public async Task<IActionResult> RefreshByRiotId([FromRoute] string region, [FromRoute] string name,
        [FromRoute] string tag, CancellationToken ct)
    {
        if (!PlatformRouteParser.TryParse(region, out var platform))
            return BadRequest($"Unsupported region '{region}'. Use a platform like NA1, EUW1, EUN1, KR, etc.");

        var key = BuildRefreshKey(platform, name, tag);
        var ttl = TimeSpan.FromMinutes(5);

        var acquired = await refreshLockRepository.TryAcquireAsync(key, ttl, ct);
        if (!acquired)
        {
            var existing = await refreshLockRepository.GetAsync(key, ct);
            var seconds = existing == null
                ? (int)ttl.TotalSeconds
                : (int)Math.Max(1, (existing.LockedUntilUtc - DateTime.UtcNow).TotalSeconds);
            return Accepted(new SummonerAcceptedResponse(
                "Refresh in process",
                null,
                seconds));
        }

        // Enqueue refresh job
        backgroundJobClient.Enqueue<ISummonerRefreshJob>(job =>
            job.RefreshByRiotId(name, tag, platform, key, CancellationToken.None));

        var pollUrl = Url.ActionLink(nameof(GetByRiotId), null, new
        {
            region,
            name,
            tag
        });
        return Accepted(new SummonerAcceptedResponse(
            "Refresh queued",
            pollUrl));
    }

    private static string BuildRefreshKey(PlatformRoute platform, string name, string tag)
    {
        var nm = name.Trim().ToUpperInvariant();
        var tg = tag.Trim().ToUpperInvariant();
        return $"summoner-refresh:{platform}:{nm}:{tg}";
    }

    /// <summary>
    /// Resolves champion ID to name. Phase 3 will add proper static data service.
    /// For now, returns placeholder that clients can resolve client-side.
    /// </summary>
    private static string ResolveChampionName(int championId)
    {
        return $"Champion {championId}";
    }
}
