using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Options;
using Transcendence.Data;
using Transcendence.Data.Models.LoL.Analytics;
using Transcendence.Service.Core.Services.Analytics.Interfaces;
using Transcendence.Service.Core.Services.Analytics.Models;

namespace Transcendence.Service.Core.Services.Analytics.Implementations;

public sealed class BuildLabService(
    TranscendenceContext context,
    HybridCache cache,
    IOptions<BuildLabModelingOptions> modelingOptions) : IBuildLabService
{
    private static readonly HashSet<string> Roles =
        new(["TOP", "JUNGLE", "MIDDLE", "BOTTOM", "UTILITY"], StringComparer.Ordinal);
    private static readonly HashSet<string> Sections =
        new(["ITEMS", "RUNES", "SPELLS"], StringComparer.Ordinal);
    private static readonly HashSet<string> Modes =
        new(["SUPPORTED", "IMPACT", "COMMON"], StringComparer.Ordinal);
    private static readonly HybridCacheEntryOptions CacheOptions = new()
    {
        Expiration = TimeSpan.FromMinutes(15),
        LocalCacheExpiration = TimeSpan.FromMinutes(5)
    };
    // The active pointer moves only on promotion, but every champion profile hits this lookup, so a
    // short TTL keeps the disabled/no-generation path off the database entirely.
    private static readonly HybridCacheEntryOptions GenerationCacheOptions = new()
    {
        Expiration = TimeSpan.FromSeconds(60),
        LocalCacheExpiration = TimeSpan.FromSeconds(30)
    };
    private static readonly BuildLabProvenanceDto EmptyProvenance =
        new(null, string.Empty, string.Empty, string.Empty, null, null, 0, "EMERALD_PLUS", [], []);

    private const string ActiveGenerationCacheKey = "analytics:build-lab:v1:active-generation";
    private const string DisabledReason = "Adjusted WPA is not enabled on this deployment.";
    private const string ShadowValidationReason =
        "Adjusted WPA is still in shadow validation for this patch.";
    private const string BaselineDefinition =
        "Realistic alternative choices at the same stage, timing and prior path.";
    // Starter set plus boots plus six legendary slots, with room for multi-piece starters.
    private const int MaximumItemPath = 12;

    public async Task<BuildLabResponse> GetAsync(BuildLabQuery query, CancellationToken ct = default)
    {
        var normalized = Normalize(query);
        if (!modelingOptions.Value.Enabled)
            return Empty(normalized, null, DisabledReason);
        return await GetAsync(normalized, await ResolveActiveGenerationAsync(ct), ct);
    }

    public async Task<ChampionRecommendationSummary> GetChampionRecommendationAsync(
        int championId,
        string role,
        int? opponentChampionId,
        string? patch,
        string? region,
        CancellationToken ct = default)
    {
        if (!modelingOptions.Value.Enabled)
            return new ChampionRecommendationSummary(
                false, EmptyProvenance, null, null, null, DisabledReason);

        BuildLabQuery normalized;
        try
        {
            normalized = Normalize(new BuildLabQuery(
                championId, role, opponentChampionId, patch, region, "ITEMS", "SUPPORTED", [], [], []));
        }
        catch (ArgumentException)
        {
            // The summary is embedded in the champion profile, so invalid context degrades to an
            // unavailable block instead of failing the whole profile read.
            return new ChampionRecommendationSummary(
                false, EmptyProvenance, null, null, null,
                "The requested Build Lab context is not valid.");
        }

        // One resolve for all three sections: a promotion between reads would otherwise mix
        // generations behind a single provenance block.
        var generation = await ResolveActiveGenerationAsync(ct);
        if (generation == null)
            return new ChampionRecommendationSummary(
                false, EmptyProvenance, null, null, null, ShadowValidationReason);

        var items = await GetAsync(normalized, generation, ct);
        var runes = await GetAsync(normalized with { Section = "RUNES" }, generation, ct);
        var spells = await GetAsync(normalized with { Section = "SPELLS" }, generation, ct);
        if (!items.Available && !runes.Available && !spells.Available)
        {
            return new ChampionRecommendationSummary(
                false,
                generation.Provenance,
                null,
                null,
                null,
                items.UnavailableReason);
        }

        return new ChampionRecommendationSummary(
            true,
            generation.Provenance,
            FirstCandidate(items, family: "FIRST_ITEM_PATH"),
            FirstCandidate(runes, family: "RUNE_PAGE"),
            FirstCandidate(spells, family: "SPELL"),
            null);
    }

    private async Task<BuildLabResponse> GetAsync(
        BuildLabQuery normalized,
        BuildLabActiveGeneration? generation,
        CancellationToken ct)
    {
        if (generation == null)
            return Empty(normalized, null, ShadowValidationReason);
        if (!PatchIsServable(generation, normalized.Patch))
            return Empty(
                normalized,
                generation,
                $"Patch {normalized.Patch} is outside the promoted generation's modeled patch set.");

        var selectedPath = SelectedPath(normalized);
        var pathHash = HashPath(selectedPath);
        var requestedRegion = NormalizeRegion(normalized.Region);
        var cacheKey =
            $"analytics:build-lab:v1:{generation.Id}:{normalized.ChampionId}:{normalized.Role}:{normalized.OpponentChampionId ?? 0}:{normalized.Patch ?? "current"}:{requestedRegion}:{normalized.Section}:{normalized.Mode}:{pathHash}";

        return await cache.GetOrCreateAsync(
            cacheKey,
            cancel => ComputeAsync(normalized, generation, requestedRegion, pathHash, cancel),
            CacheOptions,
            tags: ["analytics", $"analytics:build-lab:{generation.Id}"],
            cancellationToken: ct);
    }

    private async ValueTask<BuildLabResponse> ComputeAsync(
        BuildLabQuery query,
        BuildLabActiveGeneration generation,
        string requestedRegion,
        string pathHash,
        CancellationToken ct)
    {
        var opponentId = query.OpponentChampionId ?? 0;
        var families = query.Section switch
        {
            "ITEMS" => new[] { "STARTER", "FIRST_ITEM_PATH", "BOOTS", "ITEM" },
            "RUNES" => new[] { "RUNE_PAGE", "RUNE" },
            _ => new[] { "SPELL" }
        };

        var baseQuery = context.AdjustedActionEstimates
            .AsNoTracking()
            .Where(estimate =>
                estimate.GenerationId == generation.Id &&
                estimate.ChampionId == query.ChampionId &&
                estimate.Role == query.Role &&
                estimate.OpponentChampionId == opponentId &&
                families.Contains(estimate.DecisionFamily) &&
                estimate.PathPrefixHash == pathHash);

        var scopedRows = requestedRegion == "GLOBAL"
            ? await baseQuery.Where(estimate => estimate.RegionScope == "GLOBAL").ToListAsync(ct)
            : await baseQuery
                .Where(estimate =>
                    estimate.RegionScope == requestedRegion || estimate.RegionScope == "GLOBAL")
                .ToListAsync(ct);

        // Promotion demotes regional cells individually, so substitution has to be per cell as
        // well: one surviving regional row must not suppress every publishable global twin.
        var selectedRows = scopedRows
            .GroupBy(row => new { row.DecisionFamily, row.Stage, row.ActionKey })
            .Select(group => PreferRegional(
                group, requestedRegion, row => row.RegionScope, row => row.IsPublishable))
            .OfType<AdjustedActionEstimate>()
            .ToList();

        AdjustedPathEstimate? pathRow = null;
        if (query.Section == "ITEMS" && query.ItemPath.Count > 0)
        {
            var pathRows = await context.AdjustedPathEstimates
                .AsNoTracking()
                .Where(estimate =>
                    estimate.GenerationId == generation.Id &&
                    estimate.ChampionId == query.ChampionId &&
                    estimate.Role == query.Role &&
                    estimate.OpponentChampionId == opponentId &&
                    estimate.PathHash == pathHash &&
                    (estimate.RegionScope == requestedRegion || estimate.RegionScope == "GLOBAL"))
                .ToListAsync(ct);
            pathRow = PreferRegional(
                pathRows, requestedRegion, row => row.RegionScope, row => row.IsPublishable);
        }

        var effectiveRegion =
            selectedRows.Any(row => row.RegionScope == requestedRegion) ||
            pathRow?.RegionScope == requestedRegion
                ? requestedRegion
                : "GLOBAL";

        var stages = selectedRows
            .GroupBy(row => new { row.DecisionFamily, row.Stage })
            .OrderBy(group => FamilyOrder(group.Key.DecisionFamily))
            .ThenBy(group => group.Key.Stage)
            .Select(group => new BuildLabStageDto(
                group.Key.DecisionFamily,
                group.Key.Stage,
                StageLabel(group.Key.DecisionFamily, group.Key.Stage),
                Sort(group.Select(row => MapEstimate(row, requestedRegion)), query.Mode).ToList()))
            .ToList();

        var pathEstimate = pathRow == null
            ? null
            : new BuildLabPathEstimateDto(
                ParseIds(pathRow.ItemPathJson),
                pathRow.IsPublishable ? pathRow.EstimatedWinProbability : null,
                pathRow.IsPublishable ? pathRow.AdjustedLift : null,
                pathRow.IsPublishable ? pathRow.ConfidenceLow : null,
                pathRow.IsPublishable ? pathRow.ConfidenceHigh : null,
                pathRow.ObservedCount,
                pathRow.EffectiveSampleSize,
                pathRow.IsPublishable,
                pathRow.UnavailableReason);

        // A section is available once it can say *something*, not only once it can print a number.
        // A bucketed candidate carries a direction the posterior actually supports, which is the
        // whole reason the tier exists: a fortnightly patch rarely earns a <=3pp interval in time.
        var available =
            stages.Any(stage => stage.Candidates.Any(candidate =>
                candidate.IsPublishable || candidate.EvidenceTier == "BUCKETED")) ||
            pathEstimate is { IsPublishable: true };
        return new BuildLabResponse(
            available,
            new BuildLabContextDto(
                query.ChampionId,
                query.Role,
                query.OpponentChampionId,
                query.Patch ?? generation.Patch,
                generation.Patch,
                requestedRegion,
                effectiveRegion,
                query.Section,
                query.Mode),
            generation.Provenance,
            SelectedPath(query),
            pathEstimate,
            stages,
            available ? null : UnavailableReason(query, stages, pathEstimate));
    }

    private async ValueTask<BuildLabActiveGeneration?> ResolveActiveGenerationAsync(CancellationToken ct) =>
        await cache.GetOrCreateAsync<BuildLabActiveGeneration?>(
            ActiveGenerationCacheKey,
            async cancel =>
            {
                var generation = await context.BuildLabGenerations
                    .AsNoTracking()
                    .Where(row => row.IsActive && row.Status == BuildLabGenerationStatus.Ready)
                    .OrderByDescending(row => row.PromotedAtUtc)
                    .FirstOrDefaultAsync(cancel);
                return generation == null
                    ? null
                    : new BuildLabActiveGeneration(
                        generation.Id,
                        generation.Patch,
                        ParseStrings(generation.IncludedPatchesJson),
                        MapProvenance(generation));
            },
            GenerationCacheOptions,
            tags: ["analytics", "analytics:build-lab"],
            cancellationToken: ct);

    // Promotion retires every other generation, so a borrowed prior patch is only ever addressable
    // through the active generation's included-patch set.
    private static bool PatchIsServable(BuildLabActiveGeneration generation, string? requestedPatch) =>
        requestedPatch == null ||
        string.Equals(requestedPatch, generation.Patch, StringComparison.OrdinalIgnoreCase) ||
        generation.IncludedPatches.Any(patch =>
            string.Equals(patch, requestedPatch, StringComparison.OrdinalIgnoreCase));

    private static TRow? PreferRegional<TRow>(
        IEnumerable<TRow> rows,
        string requestedRegion,
        Func<TRow, string> scope,
        Func<TRow, bool> publishable)
        where TRow : class
    {
        var candidates = rows as IReadOnlyList<TRow> ?? rows.ToList();
        return candidates.FirstOrDefault(row => scope(row) == requestedRegion && publishable(row))
               ?? candidates.FirstOrDefault(row => scope(row) == "GLOBAL" && publishable(row))
               ?? candidates.FirstOrDefault(row => scope(row) == requestedRegion)
               ?? candidates.FirstOrDefault(row => scope(row) == "GLOBAL");
    }

    private static string UnavailableReason(
        BuildLabQuery query,
        IReadOnlyList<BuildLabStageDto> stages,
        BuildLabPathEstimateDto? pathEstimate)
    {
        if (stages.Count == 0 && pathEstimate == null)
            return query.OpponentChampionId.HasValue
                ? "This lane matchup has no modeled decisions for the selected path in the promoted generation."
                : "This champion-role scope has no modeled decisions for the selected path in the promoted generation.";

        var gated = stages
            .SelectMany(stage => stage.Candidates)
            .Select(candidate => candidate.UnavailableReason)
            .Concat([pathEstimate?.UnavailableReason])
            .FirstOrDefault(reason => !string.IsNullOrWhiteSpace(reason));
        return gated ?? (query.OpponentChampionId.HasValue
            ? "This lane-matchup path has not passed the publication gates."
            : "This champion-role path has not passed the publication gates.");
    }

    private static BuildLabQuery Normalize(BuildLabQuery query)
    {
        if (query.ChampionId <= 0)
            throw new ArgumentException("Champion id must be positive.", nameof(query));
        var role = query.Role.Trim().ToUpperInvariant();
        if (!Roles.Contains(role))
            throw new ArgumentException("Role must be TOP, JUNGLE, MIDDLE, BOTTOM, or UTILITY.", nameof(query));
        var section = query.Section.Trim().ToUpperInvariant();
        if (!Sections.Contains(section))
            throw new ArgumentException("Section must be items, runes, or spells.", nameof(query));
        var mode = query.Mode.Trim().ToUpperInvariant();
        if (!Modes.Contains(mode))
            throw new ArgumentException("Mode must be supported, impact, or common.", nameof(query));
        if (query.OpponentChampionId is <= 0)
            throw new ArgumentException("Opponent champion id must be positive.", nameof(query));

        return query with
        {
            Role = role,
            Section = section,
            Mode = mode,
            Patch = NormalizeToken(query.Patch, 32, "Patch"),
            Region = NormalizeToken(query.Region, 16, "Region"),
            ItemPath = CleanIds(query.ItemPath, MaximumItemPath, "Item path"),
            RuneSelections = CleanIds(query.RuneSelections, 12, "Rune selections"),
            SpellPair = CleanIds(query.SpellPair, 2, "Spell pair")
        };
    }

    // Both values reach length-constrained analytics columns, so reject overlong input instead of
    // letting the provider fail mid-query.
    private static string? NormalizeToken(string? value, int maximumLength, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var trimmed = value.Trim();
        if (trimmed.Length > maximumLength)
            throw new ArgumentException(
                $"{field} must be {maximumLength} characters or fewer.", nameof(value));
        if (!trimmed.All(character =>
                char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_'))
            throw new ArgumentException(
                $"{field} may only contain letters, digits, '.', '-', and '_'.", nameof(value));
        return trimmed;
    }

    private static IReadOnlyList<int> CleanIds(IReadOnlyList<int> values, int maximum, string field)
    {
        var cleaned = values.Where(value => value > 0).ToList();
        if (cleaned.Count > maximum)
            throw new ArgumentException($"{field} accepts at most {maximum} ids.", nameof(values));
        return cleaned;
    }

    private static IReadOnlyList<int> SelectedPath(BuildLabQuery query) => query.Section switch
    {
        "ITEMS" => query.ItemPath,
        "RUNES" => query.RuneSelections,
        _ => query.SpellPair
    };

    private static string NormalizeRegion(string? region) =>
        string.IsNullOrWhiteSpace(region) ||
        string.Equals(region, "ALL", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(region, "GLOBAL", StringComparison.OrdinalIgnoreCase)
            ? "GLOBAL"
            : region.Trim().ToUpperInvariant();

    public static string HashPath(IReadOnlyList<int> path)
    {
        var canonical = string.Join(",", path);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    private static AdjustedActionEstimateDto MapEstimate(
        AdjustedActionEstimate estimate,
        string requestedRegion) =>
        new(
            estimate.ActionKey,
            ParseIds(estimate.ActionIdsJson),
            estimate.IsPublishable ? estimate.AdjustedWpa : null,
            estimate.IsPublishable ? estimate.ConfidenceLow : null,
            estimate.IsPublishable ? estimate.ConfidenceHigh : null,
            // Descriptive rates are gate-conditioned too: a gated cell must not render a headline
            // win rate one click behind its own "insufficient evidence" label.
            estimate.IsPublishable ? estimate.RawWinRate : (double?)null,
            estimate.IsPublishable ? estimate.PickRate : (double?)null,
            estimate.ObservedCount,
            estimate.EffectiveSampleSize,
            estimate.AverageTimingMinutes,
            estimate.EvidenceQuality,
            estimate.RegionScope == requestedRegion ? "NONE" : "GLOBAL_FALLBACK",
            estimate.RegionScope,
            // The modeler names the comparison set it actually used per family; the const covers rows
            // written before that column was populated.
            string.IsNullOrWhiteSpace(estimate.BaselineDefinition)
                ? BaselineDefinition
                : estimate.BaselineDefinition,
            estimate.EvidenceTier.ToString().ToUpperInvariant(),
            // A bucket is only a claim at the bucketed tier: a numeric cell shows its number, and a
            // descriptive one has not earned a direction.
            estimate.EvidenceTier == EvidenceTier.Bucketed ? estimate.EvidenceBucket : null,
            estimate.IsPublishable,
            estimate.UnavailableReason);

    private static IEnumerable<AdjustedActionEstimateDto> Sort(
        IEnumerable<AdjustedActionEstimateDto> estimates,
        string mode) =>
        mode switch
        {
            "IMPACT" => estimates.OrderByDescending(estimate => estimate.AdjustedWpa ?? double.MinValue)
                .ThenByDescending(estimate => estimate.ObservedCount),
            "COMMON" => estimates.OrderByDescending(estimate => estimate.PickRate ?? double.MinValue)
                .ThenByDescending(estimate => estimate.ObservedCount),
            // Ranking is deliberately not gated on the display tier. A bucketed candidate has a
            // posterior mean worth ordering by even though its interval is too wide to print, so
            // ordering falls back to the point estimate rather than dropping the row to last.
            _ => estimates
                .OrderByDescending(estimate =>
                    estimate.ConfidenceLow ?? estimate.AdjustedWpa ?? double.MinValue)
                .ThenByDescending(estimate => estimate.EffectiveSampleSize)
        };

    private static AdjustedActionEstimateDto? FirstCandidate(BuildLabResponse response, string family) =>
        response.Stages
            .Where(stage => stage.Family == family)
            .OrderBy(stage => stage.Stage)
            .SelectMany(stage => stage.Candidates)
            .FirstOrDefault(candidate => candidate.IsPublishable);

    private static int FamilyOrder(string family) => family switch
    {
        "STARTER" => 0,
        "FIRST_ITEM_PATH" => 1,
        "BOOTS" => 2,
        "ITEM" => 3,
        "RUNE_PAGE" => 0,
        "RUNE" => 1,
        _ => 0
    };

    private static string StageLabel(string family, int stage) => family switch
    {
        "STARTER" => "Starting items",
        "FIRST_ITEM_PATH" => "First-item path",
        "BOOTS" => "Boots",
        "ITEM" => stage switch
        {
            1 => "First item",
            2 => "Second item",
            3 => "Third item",
            4 => "Fourth item",
            5 => "Fifth item",
            6 => "Sixth item",
            _ => $"{Ordinal(stage)} item"
        },
        "RUNE_PAGE" => "Complete rune page",
        "RUNE" => $"Rune choice {stage}",
        "SPELL" => "Summoner spells",
        _ => family
    };

    private static string Ordinal(int value)
    {
        var suffix = value % 100 is >= 11 and <= 13
            ? "th"
            : (value % 10) switch
            {
                1 => "st",
                2 => "nd",
                3 => "rd",
                _ => "th"
            };
        return $"{value}{suffix}";
    }

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

    private static IReadOnlyList<string> ParseStrings(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static BuildLabProvenanceDto MapProvenance(BuildLabGeneration generation) =>
        new(
            generation.Id,
            generation.DatasetVersion,
            generation.ModelVersion,
            generation.StaticDataVersion,
            generation.SourceCutoffUtc,
            generation.CompletedAtUtc,
            generation.MatchCount,
            generation.RankScope,
            ParseStrings(generation.IncludedPatchesJson),
            ParseStrings(generation.IncludedRegionsJson));

    private static BuildLabResponse Empty(
        BuildLabQuery query,
        BuildLabActiveGeneration? generation,
        string reason) =>
        new(
            false,
            new BuildLabContextDto(
                query.ChampionId,
                query.Role,
                query.OpponentChampionId,
                query.Patch ?? generation?.Patch ?? string.Empty,
                generation?.Patch ?? query.Patch ?? string.Empty,
                NormalizeRegion(query.Region),
                NormalizeRegion(query.Region),
                query.Section,
                query.Mode),
            generation?.Provenance ?? EmptyProvenance,
            SelectedPath(query),
            null,
            [],
            reason);
}

internal sealed record BuildLabActiveGeneration(
    Guid Id,
    string Patch,
    IReadOnlyList<string> IncludedPatches,
    BuildLabProvenanceDto Provenance);
