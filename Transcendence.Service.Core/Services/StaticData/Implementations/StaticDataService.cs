using Microsoft.EntityFrameworkCore;
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

        await cacheService.GetOrCreateAsync(
            $"static:runes:{patchVersion}",
            ct => FetchAndStoreRunesAsync(patchVersion, ct),
            expiration: TimeSpan.FromDays(30),
            localExpiration: TimeSpan.FromMinutes(5),
            tags: [$"patch-{patchVersion}"],
            cancellationToken: cancellationToken);

        await cacheService.GetOrCreateAsync(
            $"static:items:{patchVersion}",
            ct => FetchAndStoreItemsAsync(patchVersion, ct),
            expiration: TimeSpan.FromDays(30),
            localExpiration: TimeSpan.FromMinutes(5),
            tags: [$"patch-{patchVersion}"],
            cancellationToken: cancellationToken);
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
        var hasRunesForPatch = await context.RuneVersions
            .AnyAsync(rv => rv.PatchVersion == patchVersion, cancellationToken);

        if (hasRunesForPatch)
            return true;

        var client = httpClientFactory.CreateClient();
        var runes = await FetchRunesForPatchAsync(client, patchVersion, cancellationToken);

        if (runes.Count == 0)
            throw new InvalidOperationException($"No rune data was returned for patch '{patchVersion}'.");

        await context.RuneVersions.AddRangeAsync(runes, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task<bool> FetchAndStoreItemsAsync(string patchVersion, CancellationToken cancellationToken)
    {
        var hasItemsForPatch = await context.ItemVersions
            .AnyAsync(iv => iv.PatchVersion == patchVersion, cancellationToken);

        if (hasItemsForPatch)
            return true;

        var client = httpClientFactory.CreateClient();
        var items = await FetchItemsForPatchAsync(client, patchVersion, cancellationToken);

        if (items.Count == 0)
            throw new InvalidOperationException($"No item data was returned for patch '{patchVersion}'.");

        await context.ItemVersions.AddRangeAsync(items, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        return true;
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

    private async Task<List<RuneVersion>> FetchRunesForPatchAsync(
        HttpClient client,
        string patch,
        CancellationToken cancellationToken)
    {
        var url =
            $"https://raw.communitydragon.org/{patch}/plugins/rcp-be-lol-game-data/global/default/v1/perks.json";

        var communityDragonRunes = await GetAndDeserializeAsync<List<CommunityDragonRune>>(client, url,
            cancellationToken);

        if (communityDragonRunes == null || communityDragonRunes.Count == 0)
        {
            logger.LogWarning("No runes returned from Community Dragon for patch {Patch}.", patch);
            return [];
        }

        return communityDragonRunes.Select(r => new RuneVersion
        {
            RuneId = r.Id,
            PatchVersion = patch,
            Name = string.IsNullOrWhiteSpace(r.Name) ? $"Rune {r.Id}" : r.Name,
            Description = r.ShortDesc ?? string.Empty
        }).ToList();
    }

    private async Task<List<ItemVersion>> FetchItemsForPatchAsync(
        HttpClient client,
        string patch,
        CancellationToken cancellationToken)
    {
        var url =
            $"https://raw.communitydragon.org/{patch}/plugins/rcp-be-lol-game-data/global/default/v1/items.json";

        var communityDragonItems = await GetAndDeserializeAsync<List<CommunityDragonItem>>(client, url,
            cancellationToken);

        if (communityDragonItems == null || communityDragonItems.Count == 0)
        {
            logger.LogWarning("No items returned from Community Dragon for patch {Patch}.", patch);
            return [];
        }

        return communityDragonItems.Select(i => new ItemVersion
        {
            ItemId = i.Id,
            PatchVersion = patch,
            Name = string.IsNullOrWhiteSpace(i.Name) ? $"Item {i.Id}" : i.Name,
            Description = i.Description ?? string.Empty,
            Tags = i.Categories ?? []
        }).ToList();
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

