using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Text.Json;
using Transcendence.Data;
using Transcendence.Data.Models.LoL.Static;
using Transcendence.Service.Core.Services.Cache;
using Transcendence.Service.Core.Services.StaticData.DTOs;
using Transcendence.Service.Core.Services.StaticData.Interfaces;

namespace Transcendence.Service.Core.Services.StaticData.Implementations;

public class StaticDataService(
    TranscendenceContext context,
    IHttpClientFactory httpClientFactory,
    ICacheService cacheService,
    ILogger<StaticDataService> logger)
    : IStaticDataService
{
    private sealed class NonCacheablePatchFallbackException(string message) : Exception(message);

    private static readonly JsonSerializerOptions CaseInsensitiveJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task UpdateStaticDataAsync(CancellationToken cancellationToken = default)
    {
        var latestPatch = await GetLatestPatchVersionAsync(cancellationToken);
        if (latestPatch == null)
            return;

        await EnsureStaticDataForPatchAsync(latestPatch, cancellationToken);
    }

    public async Task DetectAndRefreshAsync(CancellationToken cancellationToken = default)
    {
        var latestPatch = await GetLatestPatchVersionAsync(cancellationToken);
        if (latestPatch == null)
            return;

        var currentPatch = await context.Patches
            .FirstOrDefaultAsync(p => p.IsActive, cancellationToken);

        if (currentPatch != null && currentPatch.Version == latestPatch)
            return;

        if (currentPatch != null)
            currentPatch.IsActive = false;

        context.Patches.Add(new Patch
        {
            Version = latestPatch,
            ReleaseDate = DateTime.UtcNow,
            DetectedAt = DateTime.UtcNow,
            IsActive = true
        });

        await context.SaveChangesAsync(cancellationToken);

        if (currentPatch != null)
            await cacheService.RemoveByTagAsync($"patch-{currentPatch.Version}", cancellationToken);

        await EnsureStaticDataForPatchAsync(latestPatch, cancellationToken);
    }

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
            ct => FetchStoreRunesAndValidateCacheabilityAsync(patchVersion, ct),
            cancellationToken);

        await MemoizeStaticDataIfAuthoritativeAsync(
            $"static:items:v2:{patchVersion}",
            patchVersion,
            ct => FetchStoreItemsAndValidateCacheabilityAsync(patchVersion, ct),
            cancellationToken);
    }

    private async Task MemoizeStaticDataIfAuthoritativeAsync(
        string cacheKey,
        string patchVersion,
        Func<CancellationToken, Task<bool>> fetchAndStore,
        CancellationToken cancellationToken)
    {
        try
        {
            await cacheService.GetOrCreateAsync(
                cacheKey,
                fetchAndStore,
                expiration: TimeSpan.FromDays(30),
                localExpiration: TimeSpan.FromMinutes(5),
                tags: [$"patch-{patchVersion}"],
                cancellationToken: cancellationToken);
        }
        catch (NonCacheablePatchFallbackException ex)
        {
            logger.LogWarning(ex,
                "Community Dragon data for patch '{PatchVersion}' used the 'latest' fallback and was not memoized.",
                patchVersion);
        }
    }

    private async Task<bool> FetchStoreRunesAndValidateCacheabilityAsync(string patchVersion, CancellationToken cancellationToken)
    {
        var shouldCache = await FetchAndStoreRunesAsync(patchVersion, cancellationToken);
        if (!shouldCache)
            throw new NonCacheablePatchFallbackException($"Rune static data for patch '{patchVersion}' used 'latest' fallback.");

        return true;
    }

    private async Task<bool> FetchStoreItemsAndValidateCacheabilityAsync(string patchVersion, CancellationToken cancellationToken)
    {
        var shouldCache = await FetchAndStoreItemsAsync(patchVersion, cancellationToken);
        if (!shouldCache)
            throw new NonCacheablePatchFallbackException($"Item static data for patch '{patchVersion}' used 'latest' fallback.");

        return true;
    }

    private async Task<string?> GetLatestPatchVersionAsync(CancellationToken cancellationToken)
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
                var resolvedPathId = isStatSlot ? 5000 : style.Id;
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
