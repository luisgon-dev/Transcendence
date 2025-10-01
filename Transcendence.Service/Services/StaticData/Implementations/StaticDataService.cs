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

        foreach (var patch in patches)
        {
            if (!await context.Patches.AnyAsync(p => p.Version == patch.Patch, cancellationToken))
            {
                context.Patches.Add(new Patch { Version = patch.Patch, ReleaseDate = DateTime.UtcNow });

                var runes = await FetchRunesForPatchAsync(client, patch.Patch, cancellationToken);
                await context.RuneVersions.AddRangeAsync(runes, cancellationToken);

                var items = await FetchItemsForPatchAsync(client, patch.Patch, cancellationToken);
                await context.ItemVersions.AddRangeAsync(items, cancellationToken);

                await context.SaveChangesAsync(cancellationToken);
            }
        }
    }

    private async Task<List<CommunityDragonPatch>> FetchPatchesAsync(HttpClient client, CancellationToken cancellationToken)
    {
        var response = await client.GetAsync("https://www.communitydragon.org/patches.json", cancellationToken);
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonSerializer.Deserialize<List<CommunityDragonPatch>>(content);
    }

    private async Task<List<RuneVersion>> FetchRunesForPatchAsync(HttpClient client, string patch, CancellationToken cancellationToken)
    {
        var response = await client.GetAsync($"https://raw.communitydragon.org/{patch}/plugins/rcp-be-lol-game-data/global/default/v1/perks.json", cancellationToken);
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        var communityDragonRunes = JsonSerializer.Deserialize<List<CommunityDragonRune>>(content);

        return communityDragonRunes.Select(r => new RuneVersion
        {
            RuneId = r.Id,
            PatchVersion = patch,
            Key = r.Key,
            Name = r.Name,
            Description = r.ShortDesc,
            RunePathId = r.RunePathId,
            RunePathName = r.RunePathName,
            Slot = r.Slot
        }).ToList();
    }

    private async Task<List<ItemVersion>> FetchItemsForPatchAsync(HttpClient client, string patch, CancellationToken cancellationToken)
    {
        var response = await client.GetAsync($"https://raw.communitydragon.org/{patch}/plugins/rcp-be-lol-game-data/global/default/v1/items.json", cancellationToken);
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        var communityDragonItems = JsonSerializer.Deserialize<List<CommunityDragonItem>>(content);

        return communityDragonItems.Select(i => new ItemVersion
        {
            ItemId = i.Id,
            PatchVersion = patch,
            Name = i.Name,
            Description = i.Description,
            Tags = i.Tags
        }).ToList();
    }
}
