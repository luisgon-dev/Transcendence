using System;
using System.Threading;
using System.Threading.Tasks;
using Camille.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Transcendence.Data.Models.LoL.Account;
using Transcendence.Data.Repositories.Interfaces;
using Hangfire;

namespace Transcendence.WebAPI.Controllers;

[ApiController]
[Route("api/summoners")] 
public class SummonersController(
    ISummonerRepository summonerRepository,
    IRefreshLockRepository refreshLockRepository,
    IBackgroundJobClient backgroundJobClient
) : ControllerBase
{
    /// <summary>
    /// Get summoner information by Riot ID (gameName and tagLine) and platform region (e.g., NA1, EUW1).
    /// This endpoint reads from the database only. If the summoner is not found, a background refresh will be required.
    /// </summary>
    /// <param name="region">Platform route like NA1, EUW1, EUN1, KR, BR1, LA1, LA2, OC1, JP1, TR1, RU. Common short forms (na, euw, eune, kr, br, lan, las, oce, jp, tr, ru) are also accepted.</param>
    /// <param name="name">Riot game name (without #tag)</param>
    /// <param name="tag">Riot tag (without #)</param>
    [HttpGet("{region}/{name}/{tag}")]
    [ProducesResponseType(typeof(Summoner), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> GetByRiotId([FromRoute] string region, [FromRoute] string name, [FromRoute] string tag, CancellationToken ct)
    {
        if (!TryParsePlatformRoute(region, out var platform))
            return BadRequest($"Unsupported region '{region}'. Use a platform like NA1, EUW1, EUN1, KR, etc.");

        var platformRegion = platform.ToString();
        var summoner = await summonerRepository.FindByRiotIdAsync(platformRegion, name, tag, q => q.Include(s => s.Ranks).Include(s => s.HistoricalRanks), ct);
        if (summoner != null)
        {
            return Ok(summoner);
        }

        // If a refresh is in progress, let the caller know
        var refreshKey = BuildRefreshKey(platform, name, tag);
        var lockRow = await refreshLockRepository.GetAsync(refreshKey, ct);
        var pollUrl = Url.ActionLink(action: nameof(GetByRiotId), controller: null, values: new { region, name, tag });
        if (lockRow != null && lockRow.LockedUntilUtc > DateTime.UtcNow)
        {
            var seconds = (int)(lockRow.LockedUntilUtc - DateTime.UtcNow).TotalSeconds;
            return Accepted(new { message = "Refresh in process", poll = pollUrl, retryAfterSeconds = Math.Max(1, seconds) });
        }

        return Accepted(new { message = "Summoner not found in store. Use the refresh endpoint to queue a background refresh.", poll = pollUrl });
    }

    /// <summary>
    /// Queue a background refresh for the specified summoner by Riot ID. Only one refresh can be in-flight at a time.
    /// </summary>
    [HttpPost("{region}/{name}/{tag}/refresh")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> RefreshByRiotId([FromRoute] string region, [FromRoute] string name, [FromRoute] string tag, CancellationToken ct)
    {
        if (!TryParsePlatformRoute(region, out var platform))
            return BadRequest($"Unsupported region '{region}'. Use a platform like NA1, EUW1, EUN1, KR, etc.");

        var key = BuildRefreshKey(platform, name, tag);
        var ttl = TimeSpan.FromMinutes(5);

        var acquired = await refreshLockRepository.TryAcquireAsync(key, ttl, ct);
        if (!acquired)
        {
            var existing = await refreshLockRepository.GetAsync(key, ct);
            var seconds = existing == null ? (int)ttl.TotalSeconds : (int)Math.Max(1, (existing.LockedUntilUtc - DateTime.UtcNow).TotalSeconds);
            return Accepted(new { message = "Refresh in process", retryAfterSeconds = seconds });
        }

        // Enqueue refresh job
        backgroundJobClient.Enqueue<Transcendence.Service.Services.Jobs.Interfaces.ISummonerRefreshJob>(
            job => job.RefreshByRiotId(name, tag, platform, key, CancellationToken.None));

        var pollUrl = Url.ActionLink(action: nameof(GetByRiotId), controller: null, values: new { region, name, tag });
        return Accepted(new { message = "Refresh queued", poll = pollUrl });
    }

    private static string BuildRefreshKey(PlatformRoute platform, string name, string tag)
    {
        var nm = name.Trim().ToUpperInvariant();
        var tg = tag.Trim().ToUpperInvariant();
        return $"summoner-refresh:{platform}:{nm}:{tg}";
    }

    private static bool TryParsePlatformRoute(string input, out PlatformRoute platform)
    {
        // normalize
        var normalized = input.Replace(" ", string.Empty).Replace("-", string.Empty).Replace("_", string.Empty).ToUpperInvariant();

        // First try direct enum parse (handles NA1, EUW1, EUN1, KR, BR1, LA1, LA2, OC1, JP1, TR1, RU)
        if (Enum.TryParse<PlatformRoute>(normalized, true, out platform))
            return true;

        // Map common short forms to official platform routes
        platform = normalized switch
        {
            "NA" => PlatformRoute.NA1,
            "EUW" => PlatformRoute.EUW1,
            "EUNE" => PlatformRoute.EUN1,
            "KR" => PlatformRoute.KR,
            "BR" => PlatformRoute.BR1,
            "LAN" => PlatformRoute.LA1,
            "LAS" => PlatformRoute.LA2,
            "OCE" => PlatformRoute.OC1,
            "JP" => PlatformRoute.JP1,
            "TR" => PlatformRoute.TR1,
            _ => default
        };
        return platform != default;
    }
}