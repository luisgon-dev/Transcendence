using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Transcendence.Data;
using Transcendence.Data.Models.Tft.Static;
using Transcendence.Service.Core.Services.Tft.Interfaces;
using Transcendence.Service.Core.Services.Tft.Models;

namespace Transcendence.Service.Core.Services.Tft.Implementations;

public class TftStaticDataService(
    TranscendenceContext context,
    IHttpClientFactory httpClientFactory,
    ILogger<TftStaticDataService> logger) : ITftStaticDataService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task UpdateStaticDataAsync(CancellationToken ct = default)
    {
        var hadStoredData = await HasStaticDataAsync(ct);

        try
        {
            var client = httpClientFactory.CreateClient();
            await using var stream = await client.GetStreamAsync("https://raw.communitydragon.org/latest/cdragon/tft/en_us.json", ct);
            var root = await JsonSerializer.DeserializeAsync<TftRoot>(stream, JsonOptions, ct);
            if (root == null || root.SetData.Count == 0)
                return;

            var activeSet = root.SetData
                .OrderByDescending(setData => setData.Number)
                .First();
            var activeSetNumber = activeSet.Number;

            foreach (var existingSet in await context.TftSets.ToListAsync(ct))
                existingSet.IsActive = existingSet.Number == activeSetNumber;

            var setEntity = await context.TftSets.FirstOrDefaultAsync(x => x.Number == activeSetNumber, ct);
            if (setEntity == null)
            {
                setEntity = new TftSet { Number = activeSetNumber };
                context.TftSets.Add(setEntity);
            }

            setEntity.Name = activeSet.Name ?? $"Set {activeSetNumber}";
            setEntity.Mutator = activeSet.Mutator;
            setEntity.IsActive = true;
            setEntity.UpdatedAtUtc = DateTime.UtcNow;

            var patch = await context.TftPatches.FirstOrDefaultAsync(x => x.IsActive, ct)
                        ?? new TftPatch { Version = "latest", IsActive = true };
            patch.ActiveSetNumber = activeSetNumber;
            patch.ActiveSetCoreName = activeSet.Name;
            patch.DetectedAtUtc = DateTime.UtcNow;
            if (context.Entry(patch).State == EntityState.Detached)
                context.TftPatches.Add(patch);

            await UpsertChampionsAsync(activeSetNumber, activeSet.Champions, ct);
            await UpsertTraitsAsync(activeSetNumber, activeSet.Traits, ct);
            await UpsertItemsAsync(activeSetNumber, activeSet.Items, activeSet.Augments, root.Items, ct);
            await context.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (hadStoredData)
        {
            logger.LogWarning(ex, "Failed to refresh TFT static data; serving stored static data.");
        }
    }

    public async Task EnsureStaticDataAsync(CancellationToken ct = default)
    {
        var hasAnyData = await HasStaticDataAsync(ct);

        if (hasAnyData)
            return;

        await UpdateStaticDataAsync(ct);
    }

    public async Task<int?> GetActiveSetNumberAsync(CancellationToken ct = default)
    {
        await EnsureStaticDataAsync(ct);
        return await context.TftSets.Where(x => x.IsActive).Select(x => (int?)x.Number).FirstOrDefaultAsync(ct);
    }

    public Task<IReadOnlyList<TftStaticEntityDto>> GetChampionCatalogAsync(CancellationToken ct = default)
    {
        return GetCatalogWithWarmupAsync(
            setNumber => context.TftUnitVersions
                .Where(x => x.SetNumber == setNumber)
                .Select(x => new TftStaticEntityDto(x.ApiName, x.Name, null, x.Icon)),
            ct);
    }

    public Task<TftStaticEntityDto?> GetChampionByApiNameAsync(string apiName, CancellationToken ct = default) =>
        GetChampionByApiNameInternalAsync(apiName, ct);

    public Task<IReadOnlyList<TftStaticEntityDto>> GetItemCatalogAsync(CancellationToken ct = default)
    {
        return GetCatalogWithWarmupAsync(
            setNumber => context.TftItemVersions
                .Where(x => x.SetNumber == setNumber)
                .Select(x => new TftStaticEntityDto(x.ApiName, x.Name, x.Description, x.Icon)),
            ct);
    }

    public Task<TftStaticEntityDto?> GetItemByApiNameAsync(string apiName, CancellationToken ct = default) =>
        GetItemByApiNameInternalAsync(apiName, ct);

    public Task<IReadOnlyList<TftStaticEntityDto>> GetTraitCatalogAsync(CancellationToken ct = default)
    {
        return GetCatalogWithWarmupAsync(
            setNumber => context.TftTraitVersions
                .Where(x => x.SetNumber == setNumber)
                .Select(x => new TftStaticEntityDto(x.ApiName, x.Name, x.Description, x.Icon)),
            ct);
    }

    public Task<TftStaticEntityDto?> GetTraitByApiNameAsync(string apiName, CancellationToken ct = default) =>
        GetTraitByApiNameInternalAsync(apiName, ct);

    public Task<IReadOnlyList<TftStaticEntityDto>> GetAugmentCatalogAsync(CancellationToken ct = default)
    {
        return GetCatalogWithWarmupAsync(
            setNumber => context.TftAugmentVersions
                .Where(x => x.SetNumber == setNumber)
                .Select(x => new TftStaticEntityDto(x.ApiName, x.Name, x.Description, x.Icon)),
            ct);
    }

    public Task<TftStaticEntityDto?> GetAugmentByApiNameAsync(string apiName, CancellationToken ct = default) =>
        GetAugmentByApiNameInternalAsync(apiName, ct);

    private async Task<IReadOnlyList<TftStaticEntityDto>> GetCatalogWithWarmupAsync(
        Func<int, IQueryable<TftStaticEntityDto>> queryFactory,
        CancellationToken ct)
    {
        await EnsureStaticDataAsync(ct);
        var activeSet = await context.TftSets
            .AsNoTracking()
            .Where(x => x.IsActive)
            .Select(x => (int?)x.Number)
            .FirstOrDefaultAsync(ct);
        if (!activeSet.HasValue)
            return [];

        return (await queryFactory(activeSet.Value)
                .AsNoTracking()
                .ToListAsync(ct))
            .OrderBy(x => x.Name)
            .ToList();
    }

    private async Task<TftStaticEntityDto?> GetChampionByApiNameInternalAsync(string apiName, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(apiName))
            return null;

        await EnsureStaticDataAsync(ct);
        var activeSet = await GetActiveSetNumberInternalAsync(ct);
        if (!activeSet.HasValue)
            return null;

        return await context.TftUnitVersions
            .AsNoTracking()
            .Where(x => x.SetNumber == activeSet.Value && x.ApiName == apiName)
            .Select(x => new TftStaticEntityDto(x.ApiName, x.Name, null, x.Icon))
            .FirstOrDefaultAsync(ct);
    }

    private async Task<TftStaticEntityDto?> GetItemByApiNameInternalAsync(string apiName, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(apiName))
            return null;

        await EnsureStaticDataAsync(ct);
        var activeSet = await GetActiveSetNumberInternalAsync(ct);
        if (!activeSet.HasValue)
            return null;

        return await context.TftItemVersions
            .AsNoTracking()
            .Where(x => x.SetNumber == activeSet.Value && x.ApiName == apiName)
            .Select(x => new TftStaticEntityDto(x.ApiName, x.Name, x.Description, x.Icon))
            .FirstOrDefaultAsync(ct);
    }

    private async Task<TftStaticEntityDto?> GetTraitByApiNameInternalAsync(string apiName, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(apiName))
            return null;

        await EnsureStaticDataAsync(ct);
        var activeSet = await GetActiveSetNumberInternalAsync(ct);
        if (!activeSet.HasValue)
            return null;

        return await context.TftTraitVersions
            .AsNoTracking()
            .Where(x => x.SetNumber == activeSet.Value && x.ApiName == apiName)
            .Select(x => new TftStaticEntityDto(x.ApiName, x.Name, x.Description, x.Icon))
            .FirstOrDefaultAsync(ct);
    }

    private async Task<TftStaticEntityDto?> GetAugmentByApiNameInternalAsync(string apiName, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(apiName))
            return null;

        await EnsureStaticDataAsync(ct);
        var activeSet = await GetActiveSetNumberInternalAsync(ct);
        if (!activeSet.HasValue)
            return null;

        return await context.TftAugmentVersions
            .AsNoTracking()
            .Where(x => x.SetNumber == activeSet.Value && x.ApiName == apiName)
            .Select(x => new TftStaticEntityDto(x.ApiName, x.Name, x.Description, x.Icon))
            .FirstOrDefaultAsync(ct);
    }

    private async Task<bool> HasStaticDataAsync(CancellationToken ct)
    {
        return await context.TftSets.AsNoTracking().AnyAsync(ct)
               && await context.TftUnitVersions.AsNoTracking().AnyAsync(ct)
               && await context.TftItemVersions.AsNoTracking().AnyAsync(ct)
               && await context.TftTraitVersions.AsNoTracking().AnyAsync(ct)
               && await context.TftAugmentVersions.AsNoTracking().AnyAsync(ct);
    }

    private Task<int?> GetActiveSetNumberInternalAsync(CancellationToken ct) =>
        context.TftSets
            .AsNoTracking()
            .Where(x => x.IsActive)
            .Select(x => (int?)x.Number)
            .FirstOrDefaultAsync(ct);

    private async Task UpsertChampionsAsync(int setNumber, IReadOnlyList<TftChampion> champions, CancellationToken ct)
    {
        foreach (var champion in champions)
        {
            var entity = await context.TftUnitVersions.FirstOrDefaultAsync(x => x.SetNumber == setNumber && x.ApiName == champion.ApiName, ct)
                         ?? new TftUnitVersion { Id = Guid.NewGuid(), SetNumber = setNumber, ApiName = champion.ApiName ?? Guid.NewGuid().ToString("N") };
            entity.Name = champion.Name ?? champion.CharacterName ?? entity.ApiName;
            entity.CharacterName = champion.CharacterName ?? entity.Name;
            entity.Cost = champion.Cost;
            entity.Icon = champion.Icon;
            entity.SquareIcon = champion.SquareIcon;
            entity.TileIcon = champion.TileIcon;
            entity.Role = champion.Role;
            entity.Traits = champion.Traits?.Where(x => !string.IsNullOrWhiteSpace(x)).ToArray() ?? [];

            if (context.Entry(entity).State == EntityState.Detached)
                context.TftUnitVersions.Add(entity);
        }
    }

    private async Task UpsertTraitsAsync(int setNumber, IReadOnlyList<TftTrait> traits, CancellationToken ct)
    {
        foreach (var trait in traits)
        {
            var entity = await context.TftTraitVersions.FirstOrDefaultAsync(x => x.SetNumber == setNumber && x.ApiName == trait.ApiName, ct)
                         ?? new TftTraitVersion { Id = Guid.NewGuid(), SetNumber = setNumber, ApiName = trait.ApiName ?? Guid.NewGuid().ToString("N") };
            entity.Name = trait.Name ?? entity.ApiName;
            entity.Description = trait.Desc;
            entity.Icon = trait.Icon;
            if (context.Entry(entity).State == EntityState.Detached)
                context.TftTraitVersions.Add(entity);
        }
    }

    private async Task UpsertItemsAsync(int setNumber, IReadOnlyList<string> itemApiNames, IReadOnlyList<string> augmentApiNames,
        IReadOnlyList<TftItem> itemCatalog, CancellationToken ct)
    {
        var itemLookup = itemCatalog
            .Where(x => !string.IsNullOrWhiteSpace(x.ApiName))
            .ToDictionary(x => x.ApiName!, StringComparer.OrdinalIgnoreCase);

        foreach (var apiName in itemApiNames.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!itemLookup.TryGetValue(apiName, out var item))
                continue;

            var entity = await context.TftItemVersions.FirstOrDefaultAsync(x => x.SetNumber == setNumber && x.ApiName == apiName, ct)
                         ?? new TftItemVersion { Id = Guid.NewGuid(), SetNumber = setNumber, ApiName = apiName };
            entity.Name = item.Name ?? apiName;
            entity.Description = item.Desc;
            entity.Icon = item.Icon;
            entity.RiotItemId = item.Id;
            entity.AssociatedTraits = item.AssociatedTraits ?? [];
            entity.IncompatibleTraits = item.IncompatibleTraits ?? [];
            entity.Composition = item.Composition ?? [];
            entity.Tags = item.Tags ?? [];
            entity.Unique = item.Unique;
            if (context.Entry(entity).State == EntityState.Detached)
                context.TftItemVersions.Add(entity);
        }

        foreach (var apiName in augmentApiNames.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!itemLookup.TryGetValue(apiName, out var augment))
                continue;

            var entity = await context.TftAugmentVersions.FirstOrDefaultAsync(x => x.SetNumber == setNumber && x.ApiName == apiName, ct)
                         ?? new TftAugmentVersion { Id = Guid.NewGuid(), SetNumber = setNumber, ApiName = apiName };
            entity.Name = augment.Name ?? apiName;
            entity.Description = augment.Desc;
            entity.Icon = augment.Icon;
            entity.AssociatedTraits = augment.AssociatedTraits ?? [];
            entity.IncompatibleTraits = augment.IncompatibleTraits ?? [];
            entity.Tags = augment.Tags ?? [];
            entity.Unique = augment.Unique;
            if (context.Entry(entity).State == EntityState.Detached)
                context.TftAugmentVersions.Add(entity);
        }
    }

    private sealed class TftRoot
    {
        public List<TftItem> Items { get; set; } = [];
        public List<TftSetData> SetData { get; set; } = [];
    }

    private sealed class TftSetData
    {
        public List<string> Augments { get; set; } = [];
        public List<TftChampion> Champions { get; set; } = [];
        public List<string> Items { get; set; } = [];
        public string? Mutator { get; set; }
        public string? Name { get; set; }
        public int Number { get; set; }
        public List<TftTrait> Traits { get; set; } = [];
    }

    private sealed class TftChampion
    {
        public string? ApiName { get; set; }
        public string? CharacterName { get; set; }
        public int Cost { get; set; }
        public string? Icon { get; set; }
        public string? Name { get; set; }
        public string? Role { get; set; }
        public string? SquareIcon { get; set; }
        public string[]? Traits { get; set; }
        public string? TileIcon { get; set; }
    }

    private sealed class TftTrait
    {
        public string? ApiName { get; set; }
        public string? Desc { get; set; }
        public string? Icon { get; set; }
        public string? Name { get; set; }
    }

    private sealed class TftItem
    {
        public string? ApiName { get; set; }
        public string[]? AssociatedTraits { get; set; }
        public string[]? Composition { get; set; }
        public string? Desc { get; set; }
        public string? Icon { get; set; }
        public int? Id { get; set; }
        public string[]? IncompatibleTraits { get; set; }
        public string? Name { get; set; }
        public string[]? Tags { get; set; }
        public bool Unique { get; set; }
    }
}
