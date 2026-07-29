using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Transcendence.Data;
using Transcendence.Data.Models.Auth;
using Transcendence.Data.Models.LoL.Analytics;
using Transcendence.Service.Core.Services.Analytics.Interfaces;
using Transcendence.Service.Core.Services.Analytics.Models;

namespace Transcendence.Service.Core.Services.Analytics.Implementations;

public sealed class SavedBuildService(
    TranscendenceContext context,
    IOptions<SavedBuildOptions> optionsAccessor) : ISavedBuildService
{
    // Mirror BuildLabService.MaximumItemPath / its rune cap: a path the Lab accepts must be savable.
    private const int MaxItemPathIds = 12;
    private const int MaxRuneIds = 12;
    private const int MaxPatchLength = 32;
    private const int MaxRegionLength = 16;

    private static readonly HashSet<string> Roles =
        new(["TOP", "JUNGLE", "MIDDLE", "BOTTOM", "UTILITY"], StringComparer.Ordinal);
    private static readonly HashSet<string> Modes =
        new(["SUPPORTED", "IMPACT", "COMMON"], StringComparer.Ordinal);

    private readonly SavedBuildOptions options = optionsAccessor.Value;

    public async Task<SavedBuildListDto> ListAsync(
        Guid userId,
        int? page = null,
        int? pageSize = null,
        CancellationToken ct = default)
    {
        var resolvedPageSize = Math.Clamp(
            pageSize ?? options.DefaultPageSize,
            1,
            Math.Max(1, options.MaximumPageSize));
        var resolvedPage = Math.Max(1, page ?? 1);

        var query = context.UserSavedBuilds
            .AsNoTracking()
            .Where(build => build.UserAccountId == userId);
        var total = await query.CountAsync(ct);
        // Clamped against the total so an absurd page number cannot overflow the skip expression.
        var skip = (int)Math.Min((long)(resolvedPage - 1) * resolvedPageSize, total);
        var rows = await query
            .OrderByDescending(build => build.UpdatedAtUtc)
            .ThenByDescending(build => build.Id)
            .Skip(skip)
            .Take(resolvedPageSize)
            .ToListAsync(ct);

        var scope = await ActiveScopeAsync(ct);
        var items = await MapManyAsync(rows, scope, ct);
        return new SavedBuildListDto(items, resolvedPage, resolvedPageSize, total, skip + rows.Count < total);
    }

    public async Task<SavedBuildDto> CreateAsync(
        Guid userId,
        SaveBuildRequest request,
        CancellationToken ct = default)
    {
        var normalized = Normalize(request);
        var existing = await context.UserSavedBuilds.CountAsync(build => build.UserAccountId == userId, ct);
        if (existing >= options.MaximumPerUser)
            throw new SavedBuildLimitExceededException(options.MaximumPerUser);

        var scope = await ActiveScopeAsync(ct);
        var now = DateTime.UtcNow;
        var entity = new UserSavedBuild
        {
            Id = Guid.NewGuid(),
            UserAccountId = userId,
            UserAccount = null!,
            Name = normalized.Name,
            ChampionId = normalized.ChampionId,
            Role = normalized.Role,
            OpponentChampionId = normalized.OpponentChampionId,
            Patch = ResolvePatch(normalized.Patch, scope),
            Region = NormalizeRegion(normalized.Region),
            RankingMode = normalized.RankingMode ?? "SUPPORTED",
            ItemPathJson = JsonSerializer.Serialize(normalized.ItemPath ?? []),
            RuneSelectionsJson = JsonSerializer.Serialize(normalized.RuneSelections ?? []),
            Spell1Id = normalized.Spell1Id,
            Spell2Id = normalized.Spell2Id,
            SourceGenerationId = scope.GenerationId,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        await CaptureBaselineAsync(entity, scope, ct);
        context.UserSavedBuilds.Add(entity);
        await context.SaveChangesAsync(ct);
        return await MapOneAsync(entity, scope, ct);
    }

    public async Task<SavedBuildDto?> UpdateAsync(
        Guid userId,
        Guid savedBuildId,
        SaveBuildRequest request,
        CancellationToken ct = default)
    {
        var entity = await context.UserSavedBuilds
            .FirstOrDefaultAsync(build => build.Id == savedBuildId && build.UserAccountId == userId, ct);
        if (entity == null)
            return null;

        var normalized = Normalize(request);
        var scope = await ActiveScopeAsync(ct);
        entity.Name = normalized.Name;
        entity.ChampionId = normalized.ChampionId;
        entity.Role = normalized.Role;
        entity.OpponentChampionId = normalized.OpponentChampionId;
        entity.Patch = ResolvePatch(normalized.Patch, scope);
        entity.Region = NormalizeRegion(normalized.Region);
        entity.RankingMode = normalized.RankingMode ?? "SUPPORTED";
        entity.ItemPathJson = JsonSerializer.Serialize(normalized.ItemPath ?? []);
        entity.RuneSelectionsJson = JsonSerializer.Serialize(normalized.RuneSelections ?? []);
        entity.Spell1Id = normalized.Spell1Id;
        entity.Spell2Id = normalized.Spell2Id;
        entity.SourceGenerationId = scope.GenerationId;
        entity.UpdatedAtUtc = DateTime.UtcNow;
        await CaptureBaselineAsync(entity, scope, ct);
        await context.SaveChangesAsync(ct);
        return await MapOneAsync(entity, scope, ct);
    }

    public async Task<SavedBuildDto?> RepairAsync(
        Guid userId,
        Guid savedBuildId,
        SavedBuildRepairRequest request,
        CancellationToken ct = default)
    {
        var entity = await context.UserSavedBuilds
            .FirstOrDefaultAsync(build => build.Id == savedBuildId && build.UserAccountId == userId, ct);
        if (entity == null)
            return null;

        var choices = request.Choices ?? [];
        if (choices.Count is 0 or > MaxItemPathIds)
            throw new ArgumentException($"Between 1 and {MaxItemPathIds} repair choices are required.");

        var scope = await ActiveScopeAsync(ct);
        if (scope.ActivePatch.Length == 0)
            throw new ArgumentException("The active patch is unknown, so item availability cannot be resolved.");

        var itemPath = ParseIds(entity.ItemPathJson);
        var probe = itemPath
            .Concat(choices.Where(choice => choice.ReplacementItemId is > 0)
                .Select(choice => choice.ReplacementItemId!.Value))
            .Distinct()
            .ToList();
        var itemStates = await LoadItemStatesAsync(scope.ActivePatch, probe, ct);
        var unavailable = UnavailableItems(itemPath, scope.ActivePatch, itemStates)
            .Select(item => item.ItemId)
            .ToHashSet();

        var resolutions = new Dictionary<int, int?>();
        foreach (var choice in choices)
        {
            if (!unavailable.Contains(choice.ItemId))
            {
                throw new ArgumentException(
                    $"Item {choice.ItemId} is not an unavailable selection on this saved build.");
            }
            if (!resolutions.TryAdd(choice.ItemId, null))
                throw new ArgumentException($"Item {choice.ItemId} was given more than one repair choice.");

            var action = choice.Action?.Trim().ToUpperInvariant() ?? string.Empty;
            switch (action)
            {
                case "DROP":
                    if (choice.ReplacementItemId.HasValue)
                        throw new ArgumentException("A drop choice must not carry a replacement item id.");
                    break;
                case "REPLACE":
                    if (choice.ReplacementItemId is not > 0)
                        throw new ArgumentException("A replace choice requires a positive replacement item id.");
                    if (!itemStates.TryGetValue(choice.ReplacementItemId.Value, out var inStore) || !inStore)
                    {
                        throw new ArgumentException(
                            $"Replacement item {choice.ReplacementItemId.Value} is not purchasable on patch {scope.ActivePatch}.");
                    }
                    resolutions[choice.ItemId] = choice.ReplacementItemId.Value;
                    break;
                default:
                    throw new ArgumentException("Repair action must be drop or replace.");
            }
        }

        var repaired = new List<int>(itemPath.Count);
        foreach (var itemId in itemPath)
        {
            if (!resolutions.TryGetValue(itemId, out var replacement))
                repaired.Add(itemId);
            else if (replacement.HasValue)
                repaired.Add(replacement.Value);
        }

        // A repair is an explicit user edit against the live generation, so it re-anchors provenance.
        entity.ItemPathJson = JsonSerializer.Serialize(repaired);
        entity.Patch = ResolvePatch(null, scope);
        entity.SourceGenerationId = scope.GenerationId;
        entity.UpdatedAtUtc = DateTime.UtcNow;
        await CaptureBaselineAsync(entity, scope, ct);
        await context.SaveChangesAsync(ct);
        return await MapOneAsync(entity, scope, ct);
    }

    public async Task<bool> DeleteAsync(Guid userId, Guid savedBuildId, CancellationToken ct = default)
    {
        var removed = await context.UserSavedBuilds
            .Where(build => build.Id == savedBuildId && build.UserAccountId == userId)
            .ExecuteDeleteAsync(ct);
        return removed > 0;
    }

    public async Task<SavedBuildShareDto?> ShareAsync(
        Guid userId,
        Guid savedBuildId,
        CancellationToken ct = default)
    {
        var entity = await context.UserSavedBuilds
            .FirstOrDefaultAsync(build => build.Id == savedBuildId && build.UserAccountId == userId, ct);
        if (entity == null)
            return null;
        entity.ShareId ??= Guid.NewGuid();
        entity.UpdatedAtUtc = DateTime.UtcNow;
        await context.SaveChangesAsync(ct);
        return new SavedBuildShareDto(entity.ShareId.Value);
    }

    public async Task<bool> RevokeShareAsync(
        Guid userId,
        Guid savedBuildId,
        CancellationToken ct = default)
    {
        var entity = await context.UserSavedBuilds
            .FirstOrDefaultAsync(build => build.Id == savedBuildId && build.UserAccountId == userId, ct);
        if (entity == null)
            return false;
        entity.ShareId = null;
        entity.UpdatedAtUtc = DateTime.UtcNow;
        await context.SaveChangesAsync(ct);
        return true;
    }

    public async Task<SavedBuildDto?> GetSharedAsync(Guid shareId, CancellationToken ct = default)
    {
        var entity = await context.UserSavedBuilds
            .AsNoTracking()
            .FirstOrDefaultAsync(build => build.ShareId == shareId, ct);
        if (entity == null)
            return null;
        return await MapOneAsync(entity, await ActiveScopeAsync(ct), ct);
    }

    private async Task<SavedBuildDto> MapOneAsync(UserSavedBuild entity, ActiveScope scope, CancellationToken ct)
    {
        var mapped = await MapManyAsync([entity], scope, ct);
        return mapped[0];
    }

    private async Task<IReadOnlyList<SavedBuildDto>> MapManyAsync(
        IReadOnlyList<UserSavedBuild> rows,
        ActiveScope scope,
        CancellationToken ct)
    {
        if (rows.Count == 0)
            return [];

        var itemPaths = rows.ToDictionary(row => row.Id, row => ParseIds(row.ItemPathJson));
        var itemStates = await LoadItemStatesAsync(
            scope.ActivePatch,
            itemPaths.Values.SelectMany(path => path).Distinct().ToList(),
            ct);
        var estimates = await LoadPathEstimatesAsync(scope.GenerationId, rows, itemPaths.Values, ct);

        return rows
            .Select(row => Map(row, itemPaths[row.Id], scope, itemStates, estimates))
            .ToList();
    }

    private SavedBuildDto Map(
        UserSavedBuild entity,
        IReadOnlyList<int> itemPath,
        ActiveScope scope,
        IReadOnlyDictionary<int, bool> itemStates,
        IReadOnlyDictionary<string, AdjustedPathEstimate> estimates)
    {
        var unavailable = UnavailableItems(itemPath, scope.ActivePatch, itemStates);
        var compatibility = unavailable.Count > 0
            ? "ITEMS_RETIRED"
            : entity.SourceGenerationId == null
                ? "NO_SOURCE_GENERATION"
                : scope.ActivePatch.Length > 0 && entity.Patch != scope.ActivePatch
                    ? "PATCH_CHANGED"
                    : "CURRENT";

        return new SavedBuildDto(
            entity.Id,
            entity.Name,
            entity.ChampionId,
            entity.Role,
            entity.OpponentChampionId,
            entity.Patch,
            entity.Region,
            entity.RankingMode,
            itemPath,
            ParseIds(entity.RuneSelectionsJson),
            entity.Spell1Id,
            entity.Spell2Id,
            entity.SourceGenerationId,
            scope.GenerationId,
            AnalyticsMateriallyChanged(entity, itemPath, scope, estimates),
            compatibility,
            unavailable.Select(item => item.ItemId).ToList(),
            unavailable,
            entity.ShareId,
            entity.CreatedAtUtc,
            entity.UpdatedAtUtc);
    }

    /// <summary>
    /// Only a promotion that moved the saved setup's own outcome counts: publishability flipped, or the
    /// adjusted lift moved past the configured epsilon. A bare generation-id difference is not a change.
    /// </summary>
    private bool AnalyticsMateriallyChanged(
        UserSavedBuild entity,
        IReadOnlyList<int> itemPath,
        ActiveScope scope,
        IReadOnlyDictionary<string, AdjustedPathEstimate> estimates)
    {
        if (entity.SourceGenerationId == null ||
            scope.GenerationId == null ||
            entity.SourceGenerationId == scope.GenerationId)
        {
            return false;
        }

        var current = FindEstimate(entity, itemPath, estimates);
        var currentPublishable = current?.IsPublishable;
        if (entity.SourceIsPublishable != currentPublishable)
            return true;

        var currentLift = current is { IsPublishable: true } ? current.AdjustedLift : null;
        if (entity.SourceAdjustedLift == null && currentLift == null)
            return false;
        if (entity.SourceAdjustedLift == null || currentLift == null)
            return true;
        return Math.Abs(currentLift.Value - entity.SourceAdjustedLift.Value) > options.MaterialLiftDelta;
    }

    private async Task CaptureBaselineAsync(UserSavedBuild entity, ActiveScope scope, CancellationToken ct)
    {
        var itemPath = ParseIds(entity.ItemPathJson);
        var estimates = await LoadPathEstimatesAsync(scope.GenerationId, [entity], [itemPath], ct);
        var current = FindEstimate(entity, itemPath, estimates);
        entity.SourceIsPublishable = current?.IsPublishable;
        entity.SourceAdjustedLift = current is { IsPublishable: true } ? current.AdjustedLift : null;
    }

    private static AdjustedPathEstimate? FindEstimate(
        UserSavedBuild entity,
        IReadOnlyList<int> itemPath,
        IReadOnlyDictionary<string, AdjustedPathEstimate> estimates)
    {
        if (itemPath.Count == 0 || estimates.Count == 0)
            return null;
        var hash = BuildLabService.HashPath(itemPath);
        var opponentId = entity.OpponentChampionId ?? 0;
        var regional = estimates.GetValueOrDefault(
            EstimateKey(entity.ChampionId, entity.Role, opponentId, entity.Region, hash));
        var global = estimates.GetValueOrDefault(
            EstimateKey(entity.ChampionId, entity.Role, opponentId, "GLOBAL", hash));
        // Same precedence as BuildLabService.PreferRegional: a demoted regional cell must not shadow a
        // publishable global twin, or the baseline would disagree with what the Lab actually served.
        if (regional is { IsPublishable: true })
            return regional;
        if (global is { IsPublishable: true })
            return global;
        return regional ?? global;
    }

    private async Task<IReadOnlyDictionary<string, AdjustedPathEstimate>> LoadPathEstimatesAsync(
        Guid? generationId,
        IReadOnlyList<UserSavedBuild> builds,
        IEnumerable<IReadOnlyList<int>> itemPaths,
        CancellationToken ct)
    {
        if (generationId == null)
            return new Dictionary<string, AdjustedPathEstimate>();
        var hashes = itemPaths
            .Where(path => path.Count > 0)
            .Select(BuildLabService.HashPath)
            .Distinct()
            .ToList();
        if (hashes.Count == 0)
            return new Dictionary<string, AdjustedPathEstimate>();

        // Filtering on the scope columns as well is what makes this hit the
        // (GenerationId, ChampionId, Role, OpponentChampionId, RegionScope, PathHash) unique index —
        // a hash-only predicate scans every path estimate in the generation.
        var championIds = builds.Select(build => build.ChampionId).Distinct().ToList();
        var roles = builds.Select(build => build.Role).Distinct().ToList();
        var opponentIds = builds.Select(build => build.OpponentChampionId ?? 0).Distinct().ToList();
        var regions = builds.Select(build => build.Region).Append("GLOBAL").Distinct().ToList();

        var rows = await context.AdjustedPathEstimates
            .AsNoTracking()
            .Where(estimate =>
                estimate.GenerationId == generationId &&
                championIds.Contains(estimate.ChampionId) &&
                roles.Contains(estimate.Role) &&
                opponentIds.Contains(estimate.OpponentChampionId) &&
                regions.Contains(estimate.RegionScope) &&
                hashes.Contains(estimate.PathHash))
            .ToListAsync(ct);
        return rows
            .GroupBy(estimate => EstimateKey(
                estimate.ChampionId,
                estimate.Role,
                estimate.OpponentChampionId,
                estimate.RegionScope,
                estimate.PathHash))
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
    }

    private static string EstimateKey(
        int championId,
        string role,
        int opponentChampionId,
        string regionScope,
        string pathHash) =>
        $"{championId}|{role}|{opponentChampionId}|{regionScope}|{pathHash}";

    private async Task<IReadOnlyDictionary<int, bool>> LoadItemStatesAsync(
        string activePatch,
        IReadOnlyList<int> itemIds,
        CancellationToken ct)
    {
        if (itemIds.Count == 0 || activePatch.Length == 0)
            return new Dictionary<int, bool>();
        var rows = await context.ItemVersions
            .AsNoTracking()
            .Where(item => item.PatchVersion == activePatch && itemIds.Contains(item.ItemId))
            .Select(item => new { item.ItemId, item.InStore })
            .ToListAsync(ct);
        return rows.ToDictionary(item => item.ItemId, item => item.InStore);
    }

    private static IReadOnlyList<SavedBuildUnavailableItemDto> UnavailableItems(
        IReadOnlyList<int> itemPath,
        string activePatch,
        IReadOnlyDictionary<int, bool> itemStates)
    {
        if (itemPath.Count == 0 || activePatch.Length == 0)
            return [];
        var unavailable = new List<SavedBuildUnavailableItemDto>();
        foreach (var itemId in itemPath.Distinct())
        {
            if (!itemStates.TryGetValue(itemId, out var inStore))
                unavailable.Add(new SavedBuildUnavailableItemDto(itemId, "RETIRED"));
            else if (!inStore)
                unavailable.Add(new SavedBuildUnavailableItemDto(itemId, "REMOVED_FROM_STORE"));
        }
        return unavailable;
    }

    private static string ResolvePatch(string? requested, ActiveScope scope) =>
        !string.IsNullOrWhiteSpace(requested)
            ? requested
            : scope.GenerationPatch.Length > 0
                ? scope.GenerationPatch
                : scope.ActivePatch;

    private static SaveBuildRequest Normalize(SaveBuildRequest request)
    {
        var name = request.Name?.Trim() ?? string.Empty;
        if (name.Length is < 1 or > 120)
            throw new ArgumentException("Name must contain between 1 and 120 characters.", nameof(request));
        if (request.ChampionId <= 0)
            throw new ArgumentException("Champion id must be positive.", nameof(request));
        var role = request.Role?.Trim().ToUpperInvariant() ?? string.Empty;
        if (!Roles.Contains(role))
            throw new ArgumentException("Role must be TOP, JUNGLE, MIDDLE, BOTTOM, or UTILITY.", nameof(request));
        if (request.OpponentChampionId is <= 0)
            throw new ArgumentException("Opponent champion id must be positive.", nameof(request));
        var mode = string.IsNullOrWhiteSpace(request.RankingMode)
            ? "SUPPORTED"
            : request.RankingMode.Trim().ToUpperInvariant();
        if (!Modes.Contains(mode))
            throw new ArgumentException("Ranking mode must be supported, impact, or common.", nameof(request));

        var patch = request.Patch?.Trim();
        if (!string.IsNullOrEmpty(patch) &&
            (patch.Length > MaxPatchLength || !patch.All(character => char.IsAsciiDigit(character) || character == '.')))
        {
            throw new ArgumentException(
                $"Patch must be a dotted numeric version of at most {MaxPatchLength} characters.", nameof(request));
        }
        var region = NormalizeRegion(request.Region);
        if (region.Length > MaxRegionLength || !region.All(char.IsAsciiLetterOrDigit))
        {
            throw new ArgumentException(
                $"Region must be at most {MaxRegionLength} alphanumeric characters.", nameof(request));
        }

        return request with
        {
            Name = name,
            Role = role,
            Patch = patch,
            Region = region,
            RankingMode = mode,
            ItemPath = CleanIds(request.ItemPath, MaxItemPathIds, "An item path"),
            RuneSelections = CleanIds(request.RuneSelections, MaxRuneIds, "A rune selection")
        };
    }

    // Rejects instead of truncating: a silently trimmed path would be saved as a build the user never chose.
    private static IReadOnlyList<int> CleanIds(IReadOnlyList<int>? values, int maximum, string subject)
    {
        if (values == null)
            return [];
        var cleaned = values.Where(value => value > 0).ToList();
        if (cleaned.Count > maximum)
            throw new ArgumentException($"{subject} may contain at most {maximum} entries.");
        return cleaned;
    }

    private static string NormalizeRegion(string? region) =>
        string.IsNullOrWhiteSpace(region) ||
        string.Equals(region, "ALL", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(region, "GLOBAL", StringComparison.OrdinalIgnoreCase)
            ? "GLOBAL"
            : region.Trim().ToUpperInvariant();

    private static IReadOnlyList<int> ParseIds(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<List<int>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private async Task<ActiveScope> ActiveScopeAsync(CancellationToken ct)
    {
        var generation = await context.BuildLabGenerations
            .AsNoTracking()
            .Where(candidate => candidate.IsActive &&
                                candidate.Status == BuildLabGenerationStatus.Ready)
            .Select(candidate => new { candidate.Id, candidate.Patch })
            .FirstOrDefaultAsync(ct);
        var activePatch = await context.Patches
            .AsNoTracking()
            .Where(patch => patch.IsActive)
            .Select(patch => patch.Version)
            .FirstOrDefaultAsync(ct);
        return new ActiveScope(generation?.Id, generation?.Patch ?? string.Empty, activePatch ?? string.Empty);
    }

    private readonly record struct ActiveScope(Guid? GenerationId, string GenerationPatch, string ActivePatch);
}
