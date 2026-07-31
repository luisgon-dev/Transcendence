using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Transcendence.Data;
using Transcendence.Data.Models.LoL.Match;
using Transcendence.Data.Models.LoL.Static;
using Transcendence.Service.Core.Services.Cache;
using Transcendence.Service.Core.Services.Jobs.Configuration;
using Transcendence.Service.Core.Services.StaticData.DTOs;
using Transcendence.Service.Core.Services.StaticData.Interfaces;
using Transcendence.Service.Core.Services.StaticData.Models;

namespace Transcendence.Service.Core.Services.StaticData.Implementations;

public class StaticDataService(
    TranscendenceContext context,
    IHttpClientFactory httpClientFactory,
    ICacheService cacheService,
    IOptions<PatchPromotionOptions> patchPromotionOptions,
    ILogger<StaticDataService> logger)
    : IStaticDataService
{
    private static readonly JsonSerializerOptions CaseInsensitiveJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public Task<string?> GetLatestPatchVersionAsync(CancellationToken cancellationToken = default) =>
        FetchLatestPatchVersionAsync(cancellationToken);

    public async Task UpdateStaticDataAsync(CancellationToken cancellationToken = default)
    {
        var latestPatch = await FetchLatestPatchVersionAsync(cancellationToken);
        if (latestPatch == null)
            return;

        await EnsureStaticDataForPatchAsync(latestPatch, cancellationToken);
    }

    public async Task DetectAndRefreshAsync(CancellationToken cancellationToken = default)
    {
        var latestPatch = await FetchLatestPatchVersionAsync(cancellationToken);
        if (latestPatch == null)
            return;

        var currentPatch = await context.Patches
            .FirstOrDefaultAsync(p => p.IsActive, cancellationToken);

        if (currentPatch != null && currentPatch.Version == latestPatch)
            return;

        // Record the new patch + fetch its static assets immediately so it's ready when promoted, but
        // do NOT promote it yet — Data Dragon updates ahead of the game-build rollout (see
        // PatchPromotionOptions). Promotion is gated below on observed match volume across regions.
        await EnsureStaticDataForPatchAsync(latestPatch, cancellationToken);

        if (currentPatch == null)
        {
            // Bootstrap: no active patch yet, so promote immediately to get analytics running.
            await PromotePatchAsync(latestPatch, previous: null, cancellationToken);
            logger.LogInformation("Promoted patch {Latest} to active (bootstrap — no prior active patch).", latestPatch);
            return;
        }

        var newPatchRow = await context.Patches
            .FirstOrDefaultAsync(p => p.Version == latestPatch, cancellationToken);
        var detectedAtUtc = EnsureUtc(newPatchRow?.DetectedAt ?? DateTime.UtcNow);
        var ageHours = (DateTime.UtcNow - detectedAtUtc).TotalHours;

        var promotion = patchPromotionOptions.Value;
        var minPerRegion = Math.Max(1, promotion.MinMatchesPerRegionToCount);
        var regionsRolledOut = await context.Matches
            .AsNoTracking()
            .Where(m => m.Status == FetchStatus.Success && m.Patch == latestPatch && m.PlatformRegion != null)
            .GroupBy(m => m.PlatformRegion!)
            .Select(g => new { Region = g.Key, Count = g.Count() })
            .Where(x => x.Count >= minPerRegion)
            .CountAsync(cancellationToken);

        var rolledOut = regionsRolledOut >= Math.Max(1, promotion.MinRegionsRolledOut);
        var forcedByAge = ageHours >= Math.Max(1, promotion.MaxWaitHoursBeforeForcePromote);

        if (!rolledOut && !forcedByAge)
        {
            logger.LogInformation(
                "Patch {Latest} detected but NOT promoted: only {Regions}/{MinRegions} regions have >= {MinPerRegion} matches on this build (age {Age:F1}h/{MaxAge}h). Keeping {Active} active until the game build rolls out.",
                latestPatch,
                regionsRolledOut,
                promotion.MinRegionsRolledOut,
                minPerRegion,
                ageHours,
                promotion.MaxWaitHoursBeforeForcePromote,
                currentPatch.Version);
            return;
        }

        await PromotePatchAsync(latestPatch, previous: currentPatch, cancellationToken);
        logger.LogInformation(
            "Promoted patch {Latest} to active ({Regions} regions rolled out, age {Age:F1}h, forcedByAge={Forced}).",
            latestPatch,
            regionsRolledOut,
            ageHours,
            forcedByAge && !rolledOut);
    }

    private async Task PromotePatchAsync(string version, Patch? previous, CancellationToken cancellationToken)
    {
        if (previous != null)
            previous.IsActive = false;

        var row = await context.Patches.FirstOrDefaultAsync(p => p.Version == version, cancellationToken);
        if (row == null)
        {
            row = new Patch
            {
                Version = version,
                ReleaseDate = DateTime.UtcNow,
                DetectedAt = DateTime.UtcNow
            };
            context.Patches.Add(row);
        }

        row.IsActive = true;
        await context.SaveChangesAsync(cancellationToken);

        await cacheService.RemoveAsync(AnalyticsCacheKeys.ActivePatch, cancellationToken);

        if (previous != null)
            await cacheService.RemoveByTagAsync(CacheTags.ForPatch(previous.Version), cancellationToken);
    }

    private static DateTime EnsureUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);

    public async Task EnsureStaticDataForPatchAsync(string patchVersion, CancellationToken cancellationToken = default)
    {
        if (!await context.Patches.AnyAsync(p => p.Version == patchVersion, cancellationToken))
        {
            context.Patches.Add(new Patch
            {
                Version = patchVersion,
                ReleaseDate = DateTime.UtcNow,
                DetectedAt = DateTime.UtcNow,
                IsActive = false // DetectAndRefreshAsync promotes the active patch.
            });
            await context.SaveChangesAsync(cancellationToken);
        }

        await MemoizeStaticDataIfAuthoritativeAsync(
            $"static:runes:{patchVersion}",
            patchVersion,
            ct => FetchAndStoreRunesAsync(patchVersion, ct),
            cancellationToken);

        await MemoizeStaticDataIfAuthoritativeAsync(
            $"static:items:v2:{patchVersion}",
            patchVersion,
            ct => FetchAndStoreItemsAsync(patchVersion, ct),
            cancellationToken);

        await MemoizeStaticDataIfAuthoritativeAsync(
            $"static:champions:{patchVersion}",
            patchVersion,
            ct => FetchAndStoreChampionsAsync(patchVersion, ct),
            cancellationToken);
    }

    private async Task MemoizeStaticDataIfAuthoritativeAsync(
        string cacheKey,
        string patchVersion,
        Func<CancellationToken, Task<bool>> fetchAndStore,
        CancellationToken cancellationToken)
    {
        var shouldCache = await cacheService.GetOrCreateAsync(
            cacheKey,
            fetchAndStore,
            expiration: TimeSpan.FromDays(30),
            localExpiration: TimeSpan.FromMinutes(5),
            tags: [CacheTags.ForPatch(patchVersion)],
            cancellationToken: cancellationToken);

        if (!shouldCache)
        {
            // HybridCache coalesces the fetch but cannot conditionally skip its write. Remove the
            // short-lived marker immediately so a patch-specific request retries the authoritative
            // URL later instead of memoizing data returned by CommunityDragon's `latest` fallback.
            await cacheService.RemoveAsync(cacheKey, cancellationToken);
            logger.LogWarning(
                "Community Dragon data for patch '{PatchVersion}' used the 'latest' fallback and was not memoized.",
                patchVersion);
        }
    }

    private async Task<string?> FetchLatestPatchVersionAsync(CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient();
        var patches = await FetchPatchesAsync(client, cancellationToken);

        if (patches == null || patches.Count == 0)
        {
            logger.LogWarning("No patch versions returned from Data Dragon.");
            return null;
        }

        return TrimPatch(patches[0].Patch);
    }

    private async Task<bool> FetchAndStoreRunesAsync(string patchVersion, CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient();
        var runeFetchResult = await FetchRunesForPatchAsync(client, patchVersion, cancellationToken);
        var runes = runeFetchResult.Items;

        if (runes.Count == 0)
            throw new InvalidOperationException($"No rune data was returned for patch '{patchVersion}'.");

        var existingRunes = await context.RuneVersions
            .Where(rv => rv.PatchVersion == patchVersion)
            .ToDictionaryAsync(rv => rv.RuneId, cancellationToken);

        var changed = false;
        foreach (var incoming in runes)
        {
            if (existingRunes.TryGetValue(incoming.RuneId, out var existing))
            {
                if (existing.Key != incoming.Key ||
                    existing.Name != incoming.Name ||
                    existing.Description != incoming.Description ||
                    existing.RunePathId != incoming.RunePathId ||
                    existing.RunePathName != incoming.RunePathName ||
                    existing.Slot != incoming.Slot)
                {
                    existing.Key = incoming.Key;
                    existing.Name = incoming.Name;
                    existing.Description = incoming.Description;
                    existing.RunePathId = incoming.RunePathId;
                    existing.RunePathName = incoming.RunePathName;
                    existing.Slot = incoming.Slot;
                    changed = true;
                }
            }
            else
            {
                await context.RuneVersions.AddAsync(incoming, cancellationToken);
                changed = true;
            }
        }

        if (changed)
            await context.SaveChangesAsync(cancellationToken);

        return !runeFetchResult.UsedLatestFallback;
    }

    private async Task<bool> FetchAndStoreItemsAsync(string patchVersion, CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient();
        var itemFetchResult = await FetchItemsForPatchAsync(client, patchVersion, cancellationToken);
        var items = itemFetchResult.Items;

        if (items.Count == 0)
            throw new InvalidOperationException($"No item data was returned for patch '{patchVersion}'.");

        var existingItems = await context.ItemVersions
            .Where(iv => iv.PatchVersion == patchVersion)
            .ToDictionaryAsync(iv => iv.ItemId, cancellationToken);

        var changed = false;
        foreach (var incoming in items)
        {
            if (existingItems.TryGetValue(incoming.ItemId, out var existing))
            {
                var hasDiff =
                    existing.Name != incoming.Name ||
                    existing.Description != incoming.Description ||
                    !AreEqual(existing.Tags, incoming.Tags) ||
                    !AreEqual(existing.BuildsFrom, incoming.BuildsFrom) ||
                    !AreEqual(existing.BuildsInto, incoming.BuildsInto) ||
                    existing.InStore != incoming.InStore ||
                    existing.PriceTotal != incoming.PriceTotal;

                if (!hasDiff)
                    continue;

                existing.Name = incoming.Name;
                existing.Description = incoming.Description;
                existing.Tags = incoming.Tags;
                existing.BuildsFrom = incoming.BuildsFrom;
                existing.BuildsInto = incoming.BuildsInto;
                existing.InStore = incoming.InStore;
                existing.PriceTotal = incoming.PriceTotal;
                changed = true;
            }
            else
            {
                await context.ItemVersions.AddAsync(incoming, cancellationToken);
                changed = true;
            }
        }

        if (changed)
            await context.SaveChangesAsync(cancellationToken);

        return !itemFetchResult.UsedLatestFallback;
    }

    private async Task<bool> FetchAndStoreChampionsAsync(string patchVersion, CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient();
        var champions = await FetchChampionsForPatchAsync(client, patchVersion, cancellationToken);
        if (champions.Count == 0)
            throw new InvalidOperationException($"No champion data was returned for patch '{patchVersion}'.");

        var existing = await context.ChampionVersions
            .Where(cv => cv.PatchVersion == patchVersion)
            .ToDictionaryAsync(cv => cv.ChampionId, cancellationToken);

        var changed = false;
        foreach (var incoming in champions)
        {
            if (existing.TryGetValue(incoming.ChampionId, out var current))
            {
                if (current.BalanceHash == incoming.BalanceHash &&
                    current.Alias == incoming.Alias &&
                    current.Name == incoming.Name &&
                    AreEqual(current.Roles, incoming.Roles))
                    continue;

                current.BalanceHash = incoming.BalanceHash;
                current.Alias = incoming.Alias;
                current.Name = incoming.Name;
                current.Roles = incoming.Roles;
                changed = true;
            }
            else
            {
                await context.ChampionVersions.AddAsync(incoming, cancellationToken);
                changed = true;
            }
        }

        if (changed)
            await context.SaveChangesAsync(cancellationToken);

        return true;
    }

    /// <summary>
    /// Champion balance data comes from Data Dragon, not Community Dragon: Community Dragon's
    /// champion payload leaves cooldown/cost as unresolved templates ("@Cooldown@s") and zero-fills
    /// effect amounts, so none of its numbers can be diffed. Roles still come from Community Dragon,
    /// which is the only one of the two that publishes them.
    /// </summary>
    private async Task<List<ChampionVersion>> FetchChampionsForPatchAsync(
        HttpClient client,
        string patchVersion,
        CancellationToken cancellationToken)
    {
        var versions = await FetchPatchesAsync(client, cancellationToken);
        var dataDragonVersion = versions?
            .Select(v => v.Patch)
            .FirstOrDefault(v => string.Equals(TrimPatch(v), patchVersion, StringComparison.Ordinal));
        if (string.IsNullOrWhiteSpace(dataDragonVersion))
        {
            logger.LogWarning(
                "Data Dragon has no build for patch '{PatchVersion}'; champion balance data was skipped.",
                patchVersion);
            return [];
        }

        var payload = await GetAndDeserializeAsync<JsonElement>(
            client,
            $"https://ddragon.leagueoflegends.com/cdn/{dataDragonVersion}/data/en_US/championFull.json",
            cancellationToken);
        if (payload.ValueKind != JsonValueKind.Object ||
            !payload.TryGetProperty("data", out var data) ||
            data.ValueKind != JsonValueKind.Object)
        {
            logger.LogWarning("Data Dragon returned no champion data for '{Version}'.", dataDragonVersion);
            return [];
        }

        var roles = await FetchChampionRolesAsync(client, patchVersion, cancellationToken);

        var results = new List<ChampionVersion>();
        foreach (var entry in data.EnumerateObject())
        {
            var champion = entry.Value;
            if (!champion.TryGetProperty("key", out var key) ||
                !int.TryParse(key.GetString(), out var championId))
                continue;

            results.Add(new ChampionVersion
            {
                ChampionId = championId,
                PatchVersion = patchVersion,
                Alias = champion.TryGetProperty("id", out var alias) ? alias.GetString() ?? string.Empty : string.Empty,
                Name = champion.TryGetProperty("name", out var name) ? name.GetString() ?? string.Empty : string.Empty,
                BalanceHash = ComputeChampionBalanceHash(champion),
                Roles = roles.TryGetValue(championId, out var championRoles) ? championRoles : []
            });
        }

        return results;
    }

    private async Task<Dictionary<int, List<string>>> FetchChampionRolesAsync(
        HttpClient client,
        string patchVersion,
        CancellationToken cancellationToken)
    {
        const string summaryPath = "plugins/rcp-be-lol-game-data/global/default/v1/champion-summary.json";
        try
        {
            var (summary, _) = await GetCommunityDragonDataWithPatchFallbackAsync<List<CommunityDragonChampionSummary>>(
                client,
                patchVersion,
                summaryPath,
                cancellationToken);

            return (summary ?? [])
                .Where(entry => entry.Id > 0)
                .GroupBy(entry => entry.Id)
                .ToDictionary(group => group.Key, group => NormalizeStringList(group.First().Roles));
        }
        catch (HttpRequestException exception)
        {
            // Roles only feed archetype pooling, which degrades to role-level pooling without them.
            // Balance detection is the load-bearing half and must not fail with them.
            logger.LogWarning(exception, "Champion roles unavailable for patch '{PatchVersion}'.", patchVersion);
            return [];
        }
    }

    /// <summary>
    /// Hashes a canonical NUMERIC projection — base stats plus each spell's cooldown/cost/range/effect
    /// arrays — and deliberately no prose. Verified against live Data Dragon: across a cosmetic-only
    /// patch this flags 0 of 173 champions while a whole-record diff flags 10 (all `skins`); across a
    /// real balance patch it flags 11. Adding descriptions or tooltips here would reintroduce exactly
    /// that noise and silently suppress borrowing for unchanged champions.
    /// </summary>
    internal static string ComputeChampionBalanceHash(JsonElement champion)
    {
        var projection = new StringBuilder();

        if (champion.TryGetProperty("stats", out var stats) && stats.ValueKind == JsonValueKind.Object)
        {
            foreach (var stat in stats.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
            {
                projection.Append(stat.Name).Append('=');
                projection.Append(stat.Value.TryGetDouble(out var value)
                    ? Math.Round(value, 4).ToString("R", CultureInfo.InvariantCulture)
                    : stat.Value.ToString());
                projection.Append(';');
            }
        }

        projection.Append("partype=");
        projection.Append(champion.TryGetProperty("partype", out var partype) ? partype.GetString() : string.Empty);
        projection.Append('|');

        if (champion.TryGetProperty("spells", out var spells) && spells.ValueKind == JsonValueKind.Array)
        {
            foreach (var spell in spells.EnumerateArray())
            {
                projection.Append(spell.TryGetProperty("id", out var spellId) ? spellId.GetString() : string.Empty);
                projection.Append(':');
                projection.Append(spell.TryGetProperty("maxrank", out var maxRank) ? maxRank.ToString() : string.Empty);
                foreach (var field in new[] { "cooldown", "cost", "range" })
                    AppendNumericArray(projection, spell, field);
                if (spell.TryGetProperty("effect", out var effect) && effect.ValueKind == JsonValueKind.Array)
                {
                    // effect[0] is null by Data Dragon convention; ToString covers it without a branch.
                    foreach (var rank in effect.EnumerateArray())
                        projection.Append(rank.ToString()).Append(',');
                }

                projection.Append('|');
            }
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(projection.ToString()))).ToLowerInvariant();
    }

    private static void AppendNumericArray(StringBuilder projection, JsonElement spell, string property)
    {
        projection.Append(property).Append('=');
        if (spell.TryGetProperty(property, out var values) && values.ValueKind == JsonValueKind.Array)
        {
            foreach (var value in values.EnumerateArray())
            {
                projection.Append(value.TryGetDouble(out var number)
                    ? Math.Round(number, 4).ToString("R", CultureInfo.InvariantCulture)
                    : value.ToString());
                projection.Append(',');
            }
        }

        projection.Append(';');
    }

    private static string TrimPatch(string patch)
    {
        var parts = patch.Split('.');
        return parts.Length >= 2 ? $"{parts[0]}.{parts[1]}" : patch;
    }

    private async Task<List<DataDragonPatch>?> FetchPatchesAsync(HttpClient client, CancellationToken cancellationToken)
    {
        var versions = await GetAndDeserializeAsync<List<string>>(
            client,
            "https://ddragon.leagueoflegends.com/api/versions.json",
            cancellationToken);

        return versions?.Select(v => new DataDragonPatch { Patch = v }).ToList();
    }

    private async Task<PatchFetchResult<RuneVersion>> FetchRunesForPatchAsync(
        HttpClient client,
        string patch,
        CancellationToken cancellationToken)
    {
        const string perksPath = "plugins/rcp-be-lol-game-data/global/default/v1/perks.json";
        const string perkStylesPath = "plugins/rcp-be-lol-game-data/global/default/v1/perkstyles.json";

        var (communityDragonRunes, resolvedPatch) =
            await GetCommunityDragonDataWithPatchFallbackAsync<List<CommunityDragonRune>>(
                client,
                patch,
                perksPath,
                cancellationToken);
        var (communityDragonStyles, stylesResolvedPatch) =
            await GetCommunityDragonDataWithPatchFallbackAsync<CommunityDragonPerkStylesRoot>(
                client,
                patch,
                perkStylesPath,
                cancellationToken,
                preferredPatch: resolvedPatch);

        if (communityDragonRunes == null || communityDragonRunes.Count == 0)
        {
            logger.LogWarning("No runes returned from Community Dragon for patch {Patch}.", patch);
            return new PatchFetchResult<RuneVersion>([], UsedLatestFallback: false);
        }

        var runeMetadata = BuildRuneMetadataByRuneId(communityDragonStyles?.Styles ?? []);

        var usedLatestFallback = string.Equals(resolvedPatch, "latest", StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(stylesResolvedPatch, "latest", StringComparison.OrdinalIgnoreCase);

        return new PatchFetchResult<RuneVersion>(communityDragonRunes.Select(r =>
        {
            var hasMetadata = runeMetadata.TryGetValue(r.Id, out var metadata);

            return new RuneVersion
            {
                RuneId = r.Id,
                PatchVersion = patch,
                Key = r.RecommendationDescriptor,
                Name = string.IsNullOrWhiteSpace(r.Name) ? $"Rune {r.Id}" : r.Name,
                Description = !string.IsNullOrWhiteSpace(r.ShortDesc)
                    ? r.ShortDesc
                    : !string.IsNullOrWhiteSpace(r.LongDesc)
                        ? r.LongDesc
                        : r.Tooltip ?? string.Empty,
                RunePathId = hasMetadata ? metadata.PathId : 0,
                RunePathName = hasMetadata ? metadata.PathName : null,
                Slot = hasMetadata ? metadata.Slot : 0
            };
        }).ToList(), usedLatestFallback);
    }

    private static Dictionary<int, RuneStaticMetadata> BuildRuneMetadataByRuneId(
        IReadOnlyCollection<CommunityDragonPerkStyle> styles)
    {
        var metadata = new Dictionary<int, RuneStaticMetadata>();

        foreach (var style in styles)
        {
            var nonStatSlot = 0;
            var statSlot = 0;

            foreach (var slot in style.Slots)
            {
                var isStatSlot = string.Equals(slot.Type, "kStatMod", StringComparison.OrdinalIgnoreCase);
                var resolvedPathId = isStatSlot ? RunePathIds.StatMods : style.Id;
                var resolvedPathName = isStatSlot ? "Stat Mods" : style.Name;
                var resolvedSlot = isStatSlot ? statSlot : nonStatSlot;

                foreach (var runeId in slot.Perks)
                {
                    if (runeId == 0 || metadata.ContainsKey(runeId))
                        continue;

                    metadata[runeId] = new RuneStaticMetadata(resolvedPathId, resolvedPathName, resolvedSlot);
                }

                if (isStatSlot)
                    statSlot++;
                else
                    nonStatSlot++;
            }
        }

        return metadata;
    }

    private readonly record struct RuneStaticMetadata(int PathId, string PathName, int Slot);

    private async Task<PatchFetchResult<ItemVersion>> FetchItemsForPatchAsync(
        HttpClient client,
        string patch,
        CancellationToken cancellationToken)
    {
        const string itemsPath = "plugins/rcp-be-lol-game-data/global/default/v1/items.json";

        var (communityDragonItems, resolvedPatch) = await GetCommunityDragonDataWithPatchFallbackAsync<List<CommunityDragonItem>>(
            client,
            patch,
            itemsPath,
            cancellationToken);

        if (communityDragonItems == null || communityDragonItems.Count == 0)
        {
            logger.LogWarning("No items returned from Community Dragon for patch {Patch}.", patch);
            return new PatchFetchResult<ItemVersion>([], UsedLatestFallback: false);
        }

        return new PatchFetchResult<ItemVersion>(communityDragonItems.Select(i => new ItemVersion
        {
            ItemId = i.Id,
            PatchVersion = patch,
            Name = string.IsNullOrWhiteSpace(i.Name) ? $"Item {i.Id}" : i.Name,
            Description = i.Description ?? string.Empty,
            Tags = NormalizeStringList(i.Categories),
            BuildsFrom = NormalizeIntList(i.From),
            BuildsInto = NormalizeIntList(i.To),
            InStore = i.InStore ?? true,
            PriceTotal = i.PriceTotal ?? 0
        }).ToList(), string.Equals(resolvedPatch, "latest", StringComparison.OrdinalIgnoreCase));
    }

    private readonly record struct PatchFetchResult<T>(List<T> Items, bool UsedLatestFallback);

    private static List<string> NormalizeStringList(List<string>? values) =>
        (values ?? [])
        .Where(v => !string.IsNullOrWhiteSpace(v))
        .Select(v => v.Trim())
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(v => v, StringComparer.OrdinalIgnoreCase)
        .ToList();

    private static List<int> NormalizeIntList(List<int>? values) =>
        (values ?? [])
        .Where(v => v > 0)
        .Distinct()
        .OrderBy(v => v)
        .ToList();

    private static bool AreEqual<T>(IReadOnlyList<T> left, IReadOnlyList<T> right)
    {
        if (left.Count != right.Count)
            return false;

        for (var i = 0; i < left.Count; i++)
        {
            if (!EqualityComparer<T>.Default.Equals(left[i], right[i]))
                return false;
        }

        return true;
    }

    private async Task<(T? Data, string ResolvedPatch)> GetCommunityDragonDataWithPatchFallbackAsync<T>(
        HttpClient client,
        string requestedPatch,
        string relativePath,
        CancellationToken cancellationToken,
        string? preferredPatch = null)
    {
        var candidates = BuildCommunityDragonPatchCandidates(requestedPatch, preferredPatch);

        for (var i = 0; i < candidates.Count; i++)
        {
            var candidate = candidates[i];
            var url = $"https://raw.communitydragon.org/{candidate}/{relativePath}";

            try
            {
                var payload = await GetAndDeserializeAsync<T>(client, url, cancellationToken);
                if (!string.Equals(candidate, requestedPatch, StringComparison.OrdinalIgnoreCase))
                {
                    logger.LogWarning(
                        "Community Dragon static data path '{RelativePath}' was missing for patch '{RequestedPatch}'. Using '{ResolvedPatch}' instead.",
                        relativePath,
                        requestedPatch,
                        candidate);
                }

                return (payload, candidate);
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound && i < candidates.Count - 1)
            {
                logger.LogInformation(
                    "Community Dragon returned 404 for patch candidate '{PatchCandidate}' and path '{RelativePath}'. Trying fallback.",
                    candidate,
                    relativePath);
            }
        }

        var finalUrl = $"https://raw.communitydragon.org/{candidates[^1]}/{relativePath}";
        throw new HttpRequestException(
            $"Community Dragon returned 404 for all patch candidates ({string.Join(", ", candidates)}) at '{finalUrl}'.",
            null,
            HttpStatusCode.NotFound);
    }

    private static IReadOnlyList<string> BuildCommunityDragonPatchCandidates(string requestedPatch, string? preferredPatch)
    {
        var candidates = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddCandidate(string? value)
        {
            var candidate = value?.Trim();
            if (string.IsNullOrWhiteSpace(candidate) || !seen.Add(candidate))
                return;

            candidates.Add(candidate);
        }

        AddCandidate(preferredPatch);
        AddCandidate(requestedPatch);

        var trimmedPatch = TrimPatch(requestedPatch);
        AddCandidate(trimmedPatch);

        var patchParts = trimmedPatch.Split('.');
        if (patchParts.Length == 2 &&
            int.TryParse(patchParts[0], out _) &&
            int.TryParse(patchParts[1], out _))
        {
            AddCandidate($"{trimmedPatch}.1");
        }

        AddCandidate("latest");

        return candidates;
    }

    private static async Task<T?> GetAndDeserializeAsync<T>(
        HttpClient client,
        string url,
        CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<T>(stream, CaseInsensitiveJsonOptions, cancellationToken);
    }
}
