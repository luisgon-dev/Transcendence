using Transcendence.Service.Core.Services.StaticContent.Models;

namespace Transcendence.Service.Core.Services.StaticContent.Interfaces;

/// <summary>
/// Read-side access to League static content (champions, items, runes, summoner
/// spells) with server-side caching, so clients never talk to Riot's CDN.
/// </summary>
/// <remarks>
/// Separate from <c>IStaticDataService</c> on purpose: that one INGESTS static data
/// into the analytics tables (balance hashes, role pooling) and is write-side. This
/// one serves display metadata and owns nothing.
/// </remarks>
public interface IStaticContentService
{
    /// <summary>Known Data Dragon versions, newest first.</summary>
    Task<StaticVersionsResponse> GetVersionsAsync(CancellationToken cancellationToken = default);

    /// <param name="version">A Data Dragon version, or null/"latest" for the newest.</param>
    Task<IReadOnlyList<StaticChampionDto>> GetChampionsAsync(
        string? version,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StaticItemDto>> GetItemsAsync(
        string? version,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StaticRuneDto>> GetRunesAsync(
        string? version,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StaticSpellDto>> GetSpellsAsync(
        string? version,
        CancellationToken cancellationToken = default);
}
