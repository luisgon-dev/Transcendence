using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Hybrid;
using Transcendence.Service.Core.Services.StaticContent.Interfaces;
using Transcendence.Service.Core.Services.StaticContent.Models;

namespace Transcendence.Service.Core.Services.StaticContent.Implementations;

/// <summary>
/// Serves League static content from Data Dragon, cached so the CDN is hit once
/// per patch for the entire user base rather than once per client install.
/// </summary>
public partial class StaticContentService(
    IHttpClientFactory httpClientFactory,
    HybridCache cache,
    ILogger<StaticContentService> logger) : IStaticContentService
{
    /// <remarks>
    /// The one place in this service that names the CDN. Everything downstream —
    /// including every client — receives absolute URLs built here, so the host can
    /// be changed without touching a caller.
    /// </remarks>
    private const string DataDragonBase = "https://ddragon.leagueoflegends.com";

    private const string Locale = "en_US";

    /// <summary>
    /// Static content for a shipped patch never changes, so this is bounded by how
    /// fast we want to notice a NEW patch, not by staleness of an old one.
    /// </summary>
    private static readonly HybridCacheEntryOptions PatchedContentCache = new()
    {
        Expiration = TimeSpan.FromHours(24),
        LocalCacheExpiration = TimeSpan.FromHours(4)
    };

    /// <summary>
    /// The version LIST is the exception: it is how a new patch is discovered, so it
    /// expires quickly. Getting this wrong is the failure where the app keeps
    /// serving last patch's data for a day after a release.
    /// </summary>
    private static readonly HybridCacheEntryOptions VersionListCache = new()
    {
        Expiration = TimeSpan.FromMinutes(30),
        LocalCacheExpiration = TimeSpan.FromMinutes(10)
    };

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<StaticVersionsResponse> GetVersionsAsync(
        CancellationToken cancellationToken = default)
    {
        var versions = await cache.GetOrCreateAsync(
            "static-content:versions",
            async cancel => await FetchAsync<List<string>>("/api/versions.json", cancel) ?? [],
            VersionListCache,
            cancellationToken: cancellationToken);

        if (versions.Count == 0)
        {
            throw new StaticContentUnavailableException(
                "Data Dragon returned no versions, so no static content can be resolved.");
        }

        return new StaticVersionsResponse(versions[0], versions);
    }

    public Task<IReadOnlyList<StaticChampionDto>> GetChampionsAsync(
        string? version,
        CancellationToken cancellationToken = default) =>
        CachedAsync<StaticChampionDto>(
            "champions",
            version,
            async (resolved, cancel) =>
            {
                var payload = await FetchAsync<DataDragonList<DataDragonChampion>>(
                    $"/cdn/{resolved}/data/{Locale}/champion.json",
                    cancel);

                return Materialize(
                    payload,
                    champion => new StaticChampionDto(
                        ParseId(champion.Key),
                        champion.Id,
                        champion.Name,
                        champion.Title ?? string.Empty,
                        champion.Tags ?? [],
                        // Champion icons are versioned; splash art is not. That
                        // asymmetry is Riot's, and it belongs here rather than in
                        // five clients.
                        $"{DataDragonBase}/cdn/{resolved}/img/champion/{champion.Id}.png",
                        $"{DataDragonBase}/cdn/img/champion/splash/{champion.Id}_0.jpg"),
                    champion => ParseId(champion.Key) > 0);
            },
            cancellationToken);

    public Task<IReadOnlyList<StaticItemDto>> GetItemsAsync(
        string? version,
        CancellationToken cancellationToken = default) =>
        CachedAsync<StaticItemDto>(
            "items",
            version,
            async (resolved, cancel) =>
            {
                var payload = await FetchAsync<DataDragonList<DataDragonItem>>(
                    $"/cdn/{resolved}/data/{Locale}/item.json",
                    cancel);

                if (payload?.Data is null) return [];

                // Items are keyed BY ID in the document, unlike champions which are
                // keyed by handle and carry the id in `key`.
                return payload.Data
                    .Select(entry =>
                    {
                        var id = ParseId(entry.Key);
                        var item = entry.Value;
                        return id <= 0
                            ? null
                            : new StaticItemDto(
                                id,
                                item.Name,
                                StripMarkup(item.Plaintext),
                                item.Tags ?? [],
                                item.Gold?.Total ?? 0,
                                item.Gold?.Purchasable ?? false,
                                $"{DataDragonBase}/cdn/{resolved}/img/item/{id}.png");
                    })
                    .Where(dto => dto is not null)
                    .Select(dto => dto!)
                    .OrderBy(dto => dto.Id)
                    .ToList();
            },
            cancellationToken);

    public Task<IReadOnlyList<StaticRuneDto>> GetRunesAsync(
        string? version,
        CancellationToken cancellationToken = default) =>
        CachedAsync<StaticRuneDto>(
            "runes",
            version,
            async (resolved, cancel) =>
            {
                var styles = await FetchAsync<List<DataDragonRuneStyle>>(
                    $"/cdn/{resolved}/data/{Locale}/runesReforged.json",
                    cancel) ?? [];

                var runes = new List<StaticRuneDto>();
                foreach (var style in styles)
                {
                    // The style itself is addressable: `primaryStyleId` /
                    // `subStyleId` on a rune page point at these, and a client that
                    // only received individual runes would render those two as bare
                    // numbers.
                    runes.Add(new StaticRuneDto(
                        style.Id,
                        style.Key,
                        style.Name,
                        string.Empty,
                        style.Id,
                        style.Name,
                        Slot: -1,
                        IsStyle: true,
                        // Rune icon paths are relative to `img/` and NOT versioned.
                        IconUrl: $"{DataDragonBase}/cdn/img/{style.Icon}"));

                    for (var slot = 0; slot < (style.Slots?.Count ?? 0); slot++)
                    {
                        foreach (var rune in style.Slots![slot].Runes ?? [])
                        {
                            runes.Add(new StaticRuneDto(
                                rune.Id,
                                rune.Key,
                                rune.Name,
                                StripMarkup(rune.ShortDesc),
                                style.Id,
                                style.Name,
                                slot,
                                IsStyle: false,
                                $"{DataDragonBase}/cdn/img/{rune.Icon}"));
                        }
                    }
                }

                // Stat shards are NOT in runesReforged.json — Riot does not publish
                // them there — so they are appended from a static table. Without
                // them the bottom row of every rune page renders as bare numbers,
                // which is the whole problem this endpoint exists to remove.
                runes.AddRange(StatShards.All(DataDragonBase));
                return runes;
            },
            cancellationToken);

    public Task<IReadOnlyList<StaticSpellDto>> GetSpellsAsync(
        string? version,
        CancellationToken cancellationToken = default) =>
        CachedAsync<StaticSpellDto>(
            "spells",
            version,
            async (resolved, cancel) =>
            {
                var payload = await FetchAsync<DataDragonList<DataDragonSpell>>(
                    $"/cdn/{resolved}/data/{Locale}/summoner.json",
                    cancel);

                // NOTE THE ID/KEY INVERSION: `id` is the string handle
                // ("SummonerFlash") and `key` is the numeric id the game and match
                // data use, as a string. Reading `id` as the number here is the
                // classic mistake and yields nothing that joins to a match.
                return Materialize(
                    payload,
                    spell => new StaticSpellDto(
                        ParseId(spell.Key),
                        spell.Id,
                        spell.Name,
                        StripMarkup(spell.Description),
                        $"{DataDragonBase}/cdn/{resolved}/img/spell/{spell.Image?.Full ?? $"{spell.Id}.png"}"),
                    spell => ParseId(spell.Key) > 0);
            },
            cancellationToken);

    /// <summary>Resolve "latest"/null to a concrete version, then cache per version.</summary>
    private async Task<IReadOnlyList<T>> CachedAsync<T>(
        string resource,
        string? version,
        Func<string, CancellationToken, Task<IReadOnlyList<T>>> build,
        CancellationToken cancellationToken)
    {
        var resolved = await ResolveVersionAsync(version, cancellationToken);
        return await cache.GetOrCreateAsync(
            $"static-content:{resource}:{resolved}",
            async cancel => await build(resolved, cancel),
            PatchedContentCache,
            cancellationToken: cancellationToken);
    }

    private async Task<string> ResolveVersionAsync(string? version, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(version) &&
            !version.Equals("latest", StringComparison.OrdinalIgnoreCase))
        {
            // Path segment goes into an upstream URL, so it is validated rather
            // than trusted: a version is digits and dots, nothing else.
            if (!VersionPattern().IsMatch(version))
            {
                throw new InvalidStaticContentVersionException(
                    $"'{version}' is not a Data Dragon version.");
            }

            return version;
        }

        return (await GetVersionsAsync(cancellationToken)).Latest;
    }

    private async Task<T?> FetchAsync<T>(string path, CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient(nameof(StaticContentService));
        client.Timeout = TimeSpan.FromSeconds(15);

        var url = $"{DataDragonBase}{path}";
        using var response = await client.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning(
                "Data Dragon returned {Status} for {Path}", (int)response.StatusCode, path);
            throw new StaticContentUnavailableException(
                $"Data Dragon returned HTTP {(int)response.StatusCode} for {path}.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<T>(stream, Json, cancellationToken);
    }

    private static IReadOnlyList<TDto> Materialize<TSource, TDto>(
        DataDragonList<TSource>? payload,
        Func<TSource, TDto> project,
        Func<TSource, bool> keep)
    {
        if (payload?.Data is null) return [];
        return payload.Data.Values.Where(keep).Select(project).ToList();
    }

    private static int ParseId(string? raw) =>
        int.TryParse(raw, out var parsed) ? parsed : 0;

    /// <summary>Riot ships HTML and custom elements in these blurbs; serve text.</summary>
    private static string StripMarkup(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var withoutBreaks = value.Replace("<br>", " ").Replace("<br/>", " ");
        return WhitespacePattern().Replace(MarkupPattern().Replace(withoutBreaks, string.Empty), " ")
            .Trim();
    }

    [GeneratedRegex(@"^\d+(\.\d+)*$")]
    private static partial Regex VersionPattern();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex MarkupPattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespacePattern();
}

/// <summary>Data Dragon is unreachable or returned something unusable.</summary>
public class StaticContentUnavailableException(string message) : Exception(message);

/// <summary>The caller asked for a version that is not version-shaped.</summary>
public class InvalidStaticContentVersionException(string message) : Exception(message);
