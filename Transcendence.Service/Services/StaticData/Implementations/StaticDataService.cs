using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Transcendence.Data;
using Transcendence.Data.Models.LoL.Static;
using Transcendence.Service.Services.StaticData.DTOs;
using Transcendence.Service.Services.StaticData.Interfaces;
namespace Transcendence.Service.Services.StaticData.Implementations;

public class StaticDataService(TranscendenceContext context, IHttpClientFactory httpClientFactory)
    : IStaticDataService
{
    public async Task UpdateStaticDataAsync(CancellationToken cancellationToken = default)
    {
        var client = httpClientFactory.CreateClient();

        var patches = await FetchPatchesAsync(client, cancellationToken);
        if (patches is null || patches.Count == 0) return;

        // Only use the latest patch (first in the list) and trim last decimal (e.g., 15.20.1 -> 15.20)
        var latestFullPatch = patches[0].Patch;
        var latestCdragonPatch = TrimPatch(latestFullPatch);

        await EnsureStaticDataForPatchAsync(latestCdragonPatch, cancellationToken);
    }

    public async Task EnsureStaticDataForPatchAsync(string patchVersion, CancellationToken cancellationToken = default)
    {
        var client = httpClientFactory.CreateClient();

        // Ensure Patch row exists
        if (!await context.Patches.AnyAsync(p => p.Version == patchVersion, cancellationToken))
        {
            context.Patches.Add(new Patch
            {
                Version = patchVersion,
                ReleaseDate = DateTime.UtcNow
            });
        }

        // Runes for this patch
        var existingRuneIds = await context.RuneVersions
            .Where(rv => rv.PatchVersion == patchVersion)
            .Select(rv => rv.RuneId)
            .ToListAsync(cancellationToken);

        if (existingRuneIds.Count == 0)
        {
            var runes = await FetchRunesForPatchAsync(client, patchVersion, cancellationToken);
            await context.RuneVersions.AddRangeAsync(runes, cancellationToken);
        }

        // Items for this patch
        var existingItemIds = await context.ItemVersions
            .Where(iv => iv.PatchVersion == patchVersion)
            .Select(iv => iv.ItemId)
            .ToListAsync(cancellationToken);

        if (existingItemIds.Count == 0)
        {
            var items = await FetchItemsForPatchAsync(client, patchVersion, cancellationToken);
            await context.ItemVersions.AddRangeAsync(items, cancellationToken);
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    static string TrimPatch(string patch)
    {
        // Converts "15.20.1" -> "15.20" by removing the last dot segment
        var parts = patch.Split('.');
        return parts.Length >= 2 ? $"{parts[0]}.{parts[1]}" : patch;
    }

    async Task<List<DataDragonPatch>?> FetchPatchesAsync(HttpClient client, CancellationToken cancellationToken)
    {
        var response = await client.GetAsync("https://ddragon.leagueoflegends.com/api/versions.json", cancellationToken);
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        var versions = JsonSerializer.Deserialize<List<string>>(content);
        return versions?.Select(v => new DataDragonPatch
        {
            Patch = v
        }).ToList();
    }

    async Task<List<RuneVersion>> FetchRunesForPatchAsync(HttpClient client, string patch,
        CancellationToken cancellationToken)
    {
        var response =
            await client.GetAsync(
                $"https://raw.communitydragon.org/{patch}/plugins/rcp-be-lol-game-data/global/default/v1/perks.json",
                cancellationToken);
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        var communityDragonRunes = JsonSerializer.Deserialize<List<CommunityDragonRune>>(content);

        return communityDragonRunes.Select(r => new RuneVersion
        {
            RuneId = r.Id,
            PatchVersion = patch,
            Name = r.Name,
            Description = r.ShortDesc
        }).ToList();
    }

    async Task<List<ItemVersion>> FetchItemsForPatchAsync(HttpClient client, string patch,
        CancellationToken cancellationToken)
    {
        var response =
            await client.GetAsync(
                $"https://raw.communitydragon.org/{patch}/plugins/rcp-be-lol-game-data/global/default/v1/items.json",
                cancellationToken);
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        var communityDragonItems = JsonSerializer.Deserialize<List<CommunityDragonItem>>(content, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        if (communityDragonItems == null || communityDragonItems.Count == 0)
            return new List<ItemVersion>();

        return communityDragonItems.Select(i => new ItemVersion
        {
            ItemId = i.Id,
            PatchVersion = patch,
            Name = i.Name,
            Description = i.Description,
            Tags = i.Categories ?? new List<string>()
        }).ToList();
    }
}
