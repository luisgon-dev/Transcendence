using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Options;
using Transcendence.Data;
using Transcendence.Data.Models.LoL.Analytics;
using Transcendence.Service.Core.Services.Analytics.Interfaces;
using Transcendence.Service.Core.Services.Analytics.Models;

namespace Transcendence.Service.Core.Services.Analytics.Implementations;

/// <summary>
/// Reads the counts <see cref="BuildLabStatsRefresher"/> maintains and turns them into per-option win
/// rates. All the estimation is <see cref="BuildLabEstimator"/>; this class only chooses which counts
/// answer a request.
/// </summary>
public sealed class BuildLabService(
    TranscendenceContext context,
    HybridCache cache,
    IOptions<BuildLabOptions> options) : IBuildLabService
{
    public const string RankScope = "ALL_TRACKED";

    /// <summary>
    /// A matchup or region needs this many (patch-weighted) games at a decision before its own counts
    /// are used; below it the stage answers from all games and says so.
    /// </summary>
    public const double MinimumScopedStageGames = 150;

    /// <summary>Weight of the active patch and each one before it, when no single patch is requested.</summary>
    public static readonly double[] PatchRecencyWeights = [1.0, 0.6, 0.35];

    private const int MaximumOptionsPerStage = 15;
    private const double MinimumOptionGames = 5;
    private const int MaximumItemPath = BuildLabDecisions.MaximumItemStage - 1;
    private const string DisabledReason = "Build Lab is not enabled on this deployment.";

    private static readonly HashSet<string> Roles =
        new(["TOP", "JUNGLE", "MIDDLE", "BOTTOM", "UTILITY"], StringComparer.Ordinal);
    private static readonly HashSet<string> Sections = new(["ITEMS", "RUNES", "SPELLS"], StringComparer.Ordinal);
    private static readonly HashSet<string> Modes = new(["SUPPORTED", "IMPACT", "COMMON"], StringComparer.Ordinal);
    private static readonly long EmptyPrefix = BuildLabPath.Hash([]);

    private static readonly HybridCacheEntryOptions ResponseCacheOptions = new()
    {
        Expiration = TimeSpan.FromMinutes(10),
        LocalCacheExpiration = TimeSpan.FromMinutes(5)
    };
    private static readonly HybridCacheEntryOptions CoverageCacheOptions = new()
    {
        Expiration = TimeSpan.FromMinutes(5),
        LocalCacheExpiration = TimeSpan.FromMinutes(2)
    };
    private static readonly BuildLabCoverageDto EmptyCoverage = new([], [], 0, null, [], RankScope);

    public async Task<BuildLabResponse> GetAsync(BuildLabQuery query, CancellationToken ct = default)
    {
        var normalized = Normalize(query);
        if (!options.Value.Enabled)
            return Empty(normalized, EmptyCoverage, DisabledReason);
        var coverage = await CoverageAsync(normalized.Patch, ct);
        return await GetAsync(normalized, coverage, ct);
    }

    public async Task<ChampionRecommendationSummary> GetChampionRecommendationAsync(
        int championId,
        string role,
        int? opponentChampionId,
        string? patch,
        string? region,
        CancellationToken ct = default)
    {
        if (!options.Value.Enabled)
            return new ChampionRecommendationSummary(false, EmptyCoverage, null, null, null, DisabledReason);

        BuildLabQuery normalized;
        try
        {
            normalized = Normalize(new BuildLabQuery(
                championId, role, opponentChampionId, patch, region, "ITEMS", "SUPPORTED", [], []));
        }
        catch (ArgumentException)
        {
            // Embedded in the champion profile: invalid context degrades to an unavailable block
            // instead of failing the whole profile read.
            return new ChampionRecommendationSummary(
                false, EmptyCoverage, null, null, null, "The requested Build Lab context is not valid.");
        }

        var coverage = await CoverageAsync(normalized.Patch, ct);
        var items = await GetAsync(normalized, coverage, ct);
        var runes = await GetAsync(normalized with { Section = "RUNES" }, coverage, ct);
        var spells = await GetAsync(normalized with { Section = "SPELLS" }, coverage, ct);
        var firstItem = Best(items, BuildLabFamily.Item, stage: 1);
        var runePage = Best(runes, BuildLabFamily.RunePage, stage: 0);
        var spellPair = Best(spells, BuildLabFamily.Spells, stage: 0);
        var available = firstItem != null || runePage != null || spellPair != null;
        return new ChampionRecommendationSummary(
            available,
            coverage,
            firstItem,
            runePage,
            spellPair,
            available ? null : "Not enough games have been counted for this champion and role yet.");
    }

    private async Task<BuildLabResponse> GetAsync(
        BuildLabQuery query,
        BuildLabCoverageDto coverage,
        CancellationToken ct)
    {
        if (coverage.IncludedPatches.Count == 0)
            return Empty(query, coverage, "No games have been counted yet.");

        var path = SelectedPath(query);
        var cacheKey = string.Join(':',
            "analytics:build-lab:v2",
            query.ChampionId, query.Role, query.OpponentChampionId ?? 0, query.Region ?? "ALL",
            string.Join(',', coverage.IncludedPatches), query.Section, query.Mode, string.Join(',', path));
        return await cache.GetOrCreateAsync(
            cacheKey,
            cancel => ComputeAsync(query, coverage, path, cancel),
            ResponseCacheOptions,
            tags: ["analytics", "analytics:build-lab"],
            cancellationToken: ct);
    }

    private async ValueTask<BuildLabResponse> ComputeAsync(
        BuildLabQuery query,
        BuildLabCoverageDto coverage,
        IReadOnlyList<int> path,
        CancellationToken ct)
    {
        var cells = RequestedCells(query, path);
        var families = cells.Select(cell => cell.Family).Distinct().ToArray();
        var prefixes = cells.Select(cell => cell.PrefixHash).Distinct().ToArray();
        var patches = coverage.IncludedPatches.ToArray();
        var weights = patches
            .Select((patch, index) => (patch, weight: coverage.PatchWeights[index]))
            .ToDictionary(pair => pair.patch, pair => pair.weight);
        var opponent = query.OpponentChampionId ?? 0;
        // A request is answered from one narrow scope at most: a matchup if one is given, else a
        // region. Matchup-by-region is not counted; it would be empty for nearly every champion.
        var scopedRegion = opponent == 0 && query.Region != null ? query.Region : BuildLabStatsRefresher.AllRegions;
        var narrow = opponent != 0 || scopedRegion != BuildLabStatsRefresher.AllRegions;

        var rows = await context.BuildLabOptionStats.AsNoTracking()
            .Where(row =>
                row.ChampionId == query.ChampionId &&
                row.Role == query.Role &&
                (row.OpponentChampionId == 0 || row.OpponentChampionId == opponent) &&
                (row.Region == BuildLabStatsRefresher.AllRegions || row.Region == scopedRegion) &&
                prefixes.Contains(row.PrefixHash) &&
                families.Contains(row.Family) &&
                patches.Contains(row.Patch))
            .Select(row => new
            {
                row.OpponentChampionId,
                row.Region,
                row.PrefixHash,
                row.Family,
                row.Stage,
                row.Patch,
                row.ActionKey,
                row.GoldBucket,
                row.Games,
                row.Wins,
                row.TimingSecondsSum
            })
            .ToListAsync(ct);

        var stages = new List<BuildLabStageDto>();
        foreach (var stageRows in rows
                     .Where(row => cells.Any(cell => cell.Family == row.Family && cell.PrefixHash == row.PrefixHash))
                     .GroupBy(row => (row.Family, row.Stage))
                     .OrderBy(group => FamilyOrder(group.Key.Family))
                     .ThenBy(group => group.Key.Stage))
        {
            var (family, stage) = stageRows.Key;
            var timed = family is BuildLabFamily.Item or BuildLabFamily.Boots;
            List<BuildLabCount> Counts(bool scoped) => stageRows
                .Where(row => scoped
                    ? row.OpponentChampionId == opponent && row.Region == scopedRegion
                    : row.OpponentChampionId == 0 && row.Region == BuildLabStatsRefresher.AllRegions)
                .Select(row =>
                {
                    var weight = weights[row.Patch];
                    return new BuildLabCount(
                        row.ActionKey, row.GoldBucket, row.Games * weight, row.Wins * weight,
                        row.TimingSecondsSum * weight);
                })
                .ToList();

            var all = BuildLabEstimator.Estimate(Counts(scoped: false), parent: null, timed);
            var estimate = all;
            var scope = "ALL";
            var fallback = false;
            if (narrow)
            {
                var scopedCounts = Counts(scoped: true);
                if (scopedCounts.Sum(count => count.Games) >= MinimumScopedStageGames)
                {
                    estimate = BuildLabEstimator.Estimate(scopedCounts, all, timed);
                    scope = opponent != 0 ? "MATCHUP" : "REGION";
                }
                else
                {
                    fallback = true;
                }
            }
            if (estimate.Games <= 0)
                continue;

            stages.Add(new BuildLabStageDto(
                FamilyName(family),
                stage,
                StageLabel(family, stage),
                estimate.Games,
                estimate.WinRate,
                scope,
                fallback,
                BuildLabEstimator.Rank(
                        estimate.Options.Where(option => option.Games >= MinimumOptionGames), query.Mode)
                    .Take(MaximumOptionsPerStage)
                    .ToList()));
        }

        var available = stages.Any(stage => stage.Options.Count > 0);
        return new BuildLabResponse(
            available,
            Context(query),
            coverage,
            path,
            stages,
            available
                ? null
                : path.Count > 0
                    ? "No counted games followed this exact path."
                    : "Not enough games have been counted for this champion and role yet.");
    }

    /// <summary>The (family, prefix) cells one request reads.</summary>
    private static List<(BuildLabFamily Family, long PrefixHash)> RequestedCells(
        BuildLabQuery query,
        IReadOnlyList<int> path) => query.Section switch
    {
        // Starters and boots are unconditioned and shown alongside every item step; the item stage is
        // the one after the legendaries already locked.
        "ITEMS" =>
        [
            (BuildLabFamily.Starter, EmptyPrefix),
            (BuildLabFamily.Boots, EmptyPrefix),
            (BuildLabFamily.Item, BuildLabPath.Hash(path))
        ],
        // The page and the keystone are unconditioned; once a keystone is locked, every later slot is
        // read conditioned on it.
        "RUNES" => path.Count == 0
            ? [(BuildLabFamily.RunePage, EmptyPrefix), (BuildLabFamily.Rune, EmptyPrefix)]
            : [(BuildLabFamily.RunePage, EmptyPrefix), (BuildLabFamily.Rune, BuildLabPath.Hash([path[0]]))],
        _ => [(BuildLabFamily.Spells, EmptyPrefix)]
    };

    private async Task<BuildLabCoverageDto> CoverageAsync(string? requestedPatch, CancellationToken ct) =>
        await cache.GetOrCreateAsync(
            $"analytics:build-lab:v2:coverage:{requestedPatch ?? "recent"}",
            async cancel =>
            {
                var recent = await context.Patches.AsNoTracking()
                    .OrderByDescending(patch => patch.IsActive)
                    .ThenByDescending(patch => patch.ReleaseDate)
                    .Select(patch => patch.Version)
                    .Take(PatchRecencyWeights.Length)
                    .ToListAsync(cancel);
                var counted = await context.BuildLabProcessedMatches.AsNoTracking()
                    .GroupBy(match => match.Patch)
                    .Select(group => new
                    {
                        Patch = group.Key,
                        Matches = group.LongCount(),
                        Last = group.Max(match => match.ProcessedAtUtc)
                    })
                    .ToListAsync(cancel);

                List<string> patches;
                List<double> patchWeights;
                if (requestedPatch != null)
                {
                    patches = counted.Any(row => row.Patch == requestedPatch) ? [requestedPatch] : [];
                    patchWeights = patches.Select(_ => 1.0).ToList();
                }
                else
                {
                    var withCounts = recent
                        .Select((patch, index) => (patch, weight: PatchRecencyWeights[index]))
                        .Where(pair => counted.Any(row => row.Patch == pair.patch))
                        .ToList();
                    patches = withCounts.Select(pair => pair.patch).ToList();
                    patchWeights = withCounts.Select(pair => pair.weight).ToList();
                }

                var included = counted.Where(row => patches.Contains(row.Patch)).ToList();
                var regions = patches.Count == 0
                    ? []
                    : await context.BuildLabProcessedMatches.AsNoTracking()
                        .Where(processed => patches.Contains(processed.Patch))
                        .Join(context.Matches.IgnoreQueryFilters(),
                            processed => processed.MatchId,
                            match => match.Id,
                            (_, match) => match.PlatformRegion)
                        .Where(region => region != null && region != "")
                        .Distinct()
                        .OrderBy(region => region)
                        .Select(region => region!)
                        .ToListAsync(cancel);
                return new BuildLabCoverageDto(
                    patches,
                    patchWeights,
                    included.Sum(row => row.Matches),
                    included.Count == 0 ? null : included.Max(row => row.Last),
                    regions,
                    RankScope);
            },
            CoverageCacheOptions,
            tags: ["analytics", "analytics:build-lab"],
            cancellationToken: ct);

    private static BuildLabOptionDto? Best(BuildLabResponse response, BuildLabFamily family, int stage)
    {
        return response.Stages
            .FirstOrDefault(candidate => candidate.Family == FamilyName(family) && candidate.Stage == stage)?
            .Options
            .FirstOrDefault(option => !option.IsLowSample);
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

        var region = NormalizeToken(query.Region, 16, "Region")?.ToUpperInvariant();
        return query with
        {
            Role = role,
            Section = section,
            Mode = mode,
            Patch = NormalizeToken(query.Patch, 32, "Patch"),
            Region = region is null or "ALL" or "GLOBAL" ? null : region,
            ItemPath = CleanIds(query.ItemPath, MaximumItemPath, "Item path"),
            RuneSelections = CleanIds(query.RuneSelections, 1, "Rune selections")
        };
    }

    private static string? NormalizeToken(string? value, int maximumLength, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var trimmed = value.Trim();
        if (trimmed.Length > maximumLength)
            throw new ArgumentException($"{field} must be {maximumLength} characters or fewer.", nameof(value));
        if (!trimmed.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_'))
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
        _ => []
    };

    private static BuildLabContextDto Context(BuildLabQuery query) =>
        new(query.ChampionId, query.Role, query.OpponentChampionId, query.Patch, query.Region ?? "ALL",
            query.Section, query.Mode);

    private static BuildLabResponse Empty(BuildLabQuery query, BuildLabCoverageDto coverage, string reason) =>
        new(false, Context(query), coverage, SelectedPath(query), [], reason);

    public static string FamilyName(BuildLabFamily family) => family switch
    {
        BuildLabFamily.Starter => "STARTER",
        BuildLabFamily.Item => "ITEM",
        BuildLabFamily.Boots => "BOOTS",
        BuildLabFamily.RunePage => "RUNE_PAGE",
        BuildLabFamily.Rune => "RUNE",
        _ => "SPELLS"
    };

    private static int FamilyOrder(BuildLabFamily family) => family switch
    {
        BuildLabFamily.Starter => 0,
        BuildLabFamily.Item => 1,
        BuildLabFamily.Boots => 2,
        BuildLabFamily.RunePage => 0,
        BuildLabFamily.Rune => 1,
        _ => 0
    };

    private static string StageLabel(BuildLabFamily family, int stage) => family switch
    {
        BuildLabFamily.Starter => "Starting items",
        BuildLabFamily.Boots => "Boots",
        BuildLabFamily.Item => stage switch
        {
            1 => "First item",
            2 => "Second item",
            3 => "Third item",
            4 => "Fourth item",
            5 => "Fifth item",
            _ => "Sixth item"
        },
        BuildLabFamily.RunePage => "Complete rune page",
        BuildLabFamily.Rune => stage == 1 ? "Keystone" : $"Rune slot {stage}",
        _ => "Summoner spells"
    };
}
