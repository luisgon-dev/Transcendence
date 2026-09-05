using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Transcendence.Service.Core.Services.StaticContent.Implementations;
using Transcendence.Service.Core.Services.StaticContent.Interfaces;
using Transcendence.Service.Core.Services.StaticContent.Models;

namespace Transcendence.WebAPI.Controllers;

/// <summary>
/// League static content — champions, items, runes and summoner spells — so clients
/// do not fetch Riot's CDN themselves.
/// </summary>
/// <remarks>
/// <para>
/// Before this, the web app and the desktop companion both fetched Data Dragon
/// directly: a second upstream host failing independently of this API, the same
/// ~300KB champion payload downloaded per install, and no way to pin what a client
/// version sees. These responses are cached server-side, so the CDN is hit roughly
/// once per patch for the whole user base.
/// </para>
/// <para>
/// Every DTO carries an absolute <c>iconUrl</c>. Clients must not build CDN paths.
/// That is what makes moving the image bytes behind this API later a server-side
/// change with no client release.
/// </para>
/// </remarks>
[ApiController]
[Route("api/lol/static")]
[EnableRateLimiting("expensive-read")]
[ProducesResponseType(StatusCodes.Status429TooManyRequests)]
public class StaticContentController(IStaticContentService staticContent) : ControllerBase
{
    /// <summary>
    /// Known Data Dragon versions, newest first, plus which one is latest.
    /// </summary>
    /// <remarks>
    /// Cached for a short window only: this is how a new patch is discovered, so a
    /// long TTL here means serving last patch's content for a day after release.
    /// </remarks>
    [HttpGet("versions")]
    [ProducesResponseType(typeof(StaticVersionsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> GetVersions(CancellationToken ct = default) =>
        await Serve(() => staticContent.GetVersionsAsync(ct));

    /// <param name="version">A Data Dragon version, or <c>latest</c>.</param>
    [HttpGet("{version}/champions")]
    [ProducesResponseType(typeof(IReadOnlyList<StaticChampionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> GetChampions(string version, CancellationToken ct = default) =>
        await Serve(() => staticContent.GetChampionsAsync(version, ct));

    /// <param name="version">A Data Dragon version, or <c>latest</c>.</param>
    [HttpGet("{version}/items")]
    [ProducesResponseType(typeof(IReadOnlyList<StaticItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> GetItems(string version, CancellationToken ct = default) =>
        await Serve(() => staticContent.GetItemsAsync(version, ct));

    /// <summary>
    /// Runes, rune STYLES and stat shards in one list.
    /// </summary>
    /// <remarks>
    /// Styles are included because a rune page's <c>primaryStyleId</c> /
    /// <c>subStyleId</c> point at them, and stat shards because Riot does not publish
    /// them in <c>runesReforged.json</c> at all. A client receiving only individual
    /// runes renders three of the nine slots on every page as bare numbers.
    /// </remarks>
    /// <param name="version">A Data Dragon version, or <c>latest</c>.</param>
    [HttpGet("{version}/runes")]
    [ProducesResponseType(typeof(IReadOnlyList<StaticRuneDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> GetRunes(string version, CancellationToken ct = default) =>
        await Serve(() => staticContent.GetRunesAsync(version, ct));

    /// <summary>
    /// Summoner spells, keyed by the NUMERIC id that match data carries.
    /// </summary>
    /// <param name="version">A Data Dragon version, or <c>latest</c>.</param>
    [HttpGet("{version}/spells")]
    [ProducesResponseType(typeof(IReadOnlyList<StaticSpellDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> GetSpells(string version, CancellationToken ct = default) =>
        await Serve(() => staticContent.GetSpellsAsync(version, ct));

    /// <summary>
    /// Map the service's two failure modes onto status codes the client can act on.
    /// </summary>
    /// <remarks>
    /// The distinction matters to the desktop app, which classifies transport
    /// outcomes to decide whether to show an outage screen: a 400 says "your
    /// request was wrong", a 503 says "the upstream is down, try later". Collapsing
    /// both into a 500 would make a typo look like an outage.
    /// </remarks>
    private async Task<IActionResult> Serve<T>(Func<Task<T>> load)
    {
        try
        {
            return Ok(await load());
        }
        catch (InvalidStaticContentVersionException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (StaticContentUnavailableException ex)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, ex.Message);
        }
    }
}
