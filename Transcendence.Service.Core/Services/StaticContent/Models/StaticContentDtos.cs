namespace Transcendence.Service.Core.Services.StaticContent.Models;

/// <summary>
/// Display metadata for League static content, served by this API instead of by
/// every client fetching Riot's CDN directly.
/// </summary>
/// <remarks>
/// <para>
/// WHY THESE EXIST. Both the web app and the desktop companion were fetching
/// champion, item, rune and spell metadata straight from Data Dragon. That means a
/// second upstream host that fails independently of this API, every install
/// re-downloading the same ~300KB champion payload, and no way to pin what a given
/// client version sees. Serving it here fetches it once per patch for the whole
/// user base and puts it behind the same cache, auth and observability as
/// everything else.
/// </para>
/// <para>
/// EVERY DTO CARRIES ITS OWN ICON URL, absolute and ready for an
/// <c>&lt;img src&gt;</c>. Clients must never build a CDN path themselves — that is
/// what made this a platform-wide change instead of a one-line config flip. The
/// URLs currently point at Data Dragon; moving the bytes behind this API later is
/// then a server-side change with no client release, because the contract already
/// belongs to us.
/// </para>
/// <para>
/// SOURCED FROM DATA DRAGON, NOT THE DATABASE, deliberately. The
/// <c>ChampionVersion</c> / <c>ItemVersion</c> / <c>RuneVersion</c> tables exist for
/// ANALYTICS — balance hashes and role pooling — and they carry neither summoner
/// spells nor rune icon paths, so serving display metadata from them would need a
/// schema migration and a re-ingestion to answer questions those tables were never
/// shaped for. Reading the CDN and caching it costs one upstream fetch per patch
/// per 24h and leaves the analytics pipeline untouched.
/// </para>
/// </remarks>
public record StaticVersionsResponse(
    string Latest,
    IReadOnlyList<string> Versions
);

public record StaticChampionDto(
    int Id,
    /// <summary>Data Dragon's string handle ("Ahri", "MonkeyKing"). Not the display name.</summary>
    string Alias,
    string Name,
    string Title,
    IReadOnlyList<string> Tags,
    string IconUrl,
    string SplashUrl
);

public record StaticItemDto(
    int Id,
    string Name,
    /// <summary>Riot's plaintext blurb, with markup stripped. Empty when absent.</summary>
    string Description,
    IReadOnlyList<string> Tags,
    int GoldTotal,
    bool PurchasableInStore,
    string IconUrl
);

public record StaticRuneDto(
    int Id,
    string Key,
    string Name,
    string Description,
    /// <summary>Owning style id (8000 Precision, 8100 Domination, ...).</summary>
    int StyleId,
    string StyleName,
    /// <summary>Row within the style; 0 is the keystone row. -1 for a style itself.</summary>
    int Slot,
    /// <summary>True for a top-level style rather than an individual rune.</summary>
    bool IsStyle,
    string IconUrl
);

public record StaticSpellDto(
    int Id,
    /// <summary>Data Dragon's string handle ("SummonerFlash").</summary>
    string Alias,
    string Name,
    string Description,
    string IconUrl
);
