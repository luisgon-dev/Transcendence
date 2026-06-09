using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Options;
using Transcendence.Data;
using Transcendence.Service.Core.Services.Analytics.Interfaces;
using Transcendence.Service.Core.Services.Analytics.Models;
using Transcendence.Service.Core.Services.Jobs.Configuration;

namespace Transcendence.Service.Core.Services.Analytics.Implementations;

/// <summary>
/// Cached analytics service for champion win rates and statistics.
/// Uses HybridCache for 24-hour L2 and 1-hour L1 caching.
/// </summary>
public class ChampionAnalyticsService : IChampionAnalyticsService
{
    private const string WinRateCacheKeyPrefix = "analytics:champion:winrates:";
    private const string TierListCacheKeyPrefix = "analytics:tierlist:v2:";
    // v2: ordered, timing-aware sectioned builds (Build Analysis Overhaul). Bumped so stale
    // pre-overhaul payloads are not served from cache.
    private const string BuildsCacheKeyPrefix = "analytics:builds:v2:";
    private const string ProBuildsCacheKeyPrefix = "analytics:probuilds:v2:";
    private const string ProPlayrateCacheKeyPrefix = "analytics:proplayrate:";
    private const string ProRosterCacheKeyPrefix = "analytics:proroster:";
    private const string MatchupsCacheKeyPrefix = "analytics:matchups:";
    private const string AnalyticsCacheTag = "analytics";
    private const string ActivePatchCacheKey = "analytics:active-patch:v1";

    // Active-patch metadata changes ~biweekly; a short cache removes the per-request Patches lookup
    // that every analytics endpoint runs (cutting DB round-trips so reads degrade less under load).
    private static readonly HybridCacheEntryOptions ActivePatchCacheOptions = new()
    {
        Expiration = TimeSpan.FromMinutes(5),
        LocalCacheExpiration = TimeSpan.FromMinutes(2)
    };

    private sealed record ActivePatchInfo(string Version, DateTime ReleaseDate);

    // Analytics cache options: 24hr total, 1hr L1 (analytics computed from large datasets)
    private static readonly HybridCacheEntryOptions AnalyticsCacheOptions = new()
    {
        Expiration = TimeSpan.FromHours(24),
        LocalCacheExpiration = TimeSpan.FromHours(1)
    };

    private readonly TranscendenceContext _context;
    private readonly HybridCache _cache;
    private readonly IChampionAnalyticsComputeService _computeService;
    private readonly ChampionAnalyticsComputeOptions _computeOptions;
    private readonly MultiRegionIngestionOptions _multiRegionOptions;

    public ChampionAnalyticsService(
        TranscendenceContext context,
        HybridCache cache,
        IChampionAnalyticsComputeService computeService,
        IOptions<ChampionAnalyticsComputeOptions> computeOptions,
        IOptions<MultiRegionIngestionOptions> multiRegionOptions)
    {
        _context = context;
        _cache = cache;
        _computeService = computeService;
        _computeOptions = computeOptions.Value;
        _multiRegionOptions = multiRegionOptions.Value;
    }

    public async Task<ChampionWinRateSummary> GetWinRatesAsync(
        int championId,
        ChampionAnalyticsFilter filter,
        CancellationToken ct)
    {
        var patchContext = await ResolvePatchContextAsync(filter.Patch, ct);
        if (string.IsNullOrWhiteSpace(patchContext.Patch))
        {
            return new ChampionWinRateSummary(
                ChampionId: championId,
                Patch: "Unknown",
                ByRoleTier: new List<ChampionWinRateDto>(),
                Sample: BuildSampleMetadata(0, patchContext)
            );
        }

        var currentPatch = patchContext.Patch!;
        var normalizedRankTier = NormalizeRankTier(filter.RankTier);
        var normalizedFilter = filter with
        {
            RankTier = normalizedRankTier == "all" ? null : normalizedRankTier,
            Region = AnalyticsRegionCatalog.NormalizeToFilter(NormalizeAnalyticsRegion(filter.Region)),
            Role = string.IsNullOrWhiteSpace(filter.Role) ? null : filter.Role.Trim().ToUpperInvariant(),
            Patch = currentPatch
        };

        // Build cache key based on normalized filter parameters
        var cacheKey = BuildCacheKey(championId, normalizedFilter, currentPatch);

        // Get or compute win rates with caching
        var winRates = await _cache.GetOrCreateAsync(
            cacheKey,
            async cancel => await _computeService.ComputeWinRatesAsync(championId, normalizedFilter, currentPatch, cancel),
            AnalyticsCacheOptions,
            tags: new[] { AnalyticsCacheTag, $"champion:{championId}", $"patch:{currentPatch}" },
            cancellationToken: ct
        );

        var sampleSize = winRates.Sum(x => Math.Max(0, x.Games));
        return new ChampionWinRateSummary(
            ChampionId: championId,
            Patch: currentPatch,
            ByRoleTier: winRates,
            Sample: BuildSampleMetadata(sampleSize, patchContext)
        );
    }

    public async Task<TierListResponse> GetTierListAsync(
        string? role,
        string? rankTier,
        string? region,
        string? patch,
        CancellationToken ct)
    {
        var patchContext = await ResolvePatchContextAsync(patch, ct);
        var normalizedRegion = NormalizeAnalyticsRegion(region);
        if (string.IsNullOrWhiteSpace(patchContext.Patch))
        {
            return new TierListResponse(
                Patch: "Unknown",
                Role: role,
                RankTier: rankTier,
                Region: normalizedRegion,
                Entries: new List<TierListEntry>(),
                Sample: BuildSampleMetadata(0, patchContext)
            );
        }

        var currentPatch = patchContext.Patch!;
        // Normalize parameters
        var normalizedRole = string.IsNullOrEmpty(role) ? "ALL" : role.ToUpperInvariant();
        var normalizedTier = NormalizeRankTier(rankTier);
        var tierFilter = normalizedTier == "all" ? null : normalizedTier;

        // Build cache key
        var cacheKey = $"{TierListCacheKeyPrefix}{normalizedRole}:{normalizedTier}:{normalizedRegion}:{currentPatch}";
        var tags = new[] { AnalyticsCacheTag, $"patch:{currentPatch}", "tierlist" };

        // Get or compute tier list with caching
        var entries = await _cache.GetOrCreateAsync(
            cacheKey,
            async cancel => await _computeService.ComputeTierListAsync(
                normalizedRole,
                tierFilter,
                normalizedRegion,
                currentPatch,
                cancel),
            AnalyticsCacheOptions,
            tags: tags,
            cancellationToken: ct
        );

        var sampleSize = entries.Sum(x => Math.Max(0, x.Games));
        return new TierListResponse(
            Patch: currentPatch,
            Role: normalizedRole,
            RankTier: normalizedTier,
            Region: normalizedRegion,
            Entries: entries,
            Sample: BuildSampleMetadata(sampleSize, patchContext)
        );
    }

    public async Task<ChampionBuildsResponse> GetBuildsAsync(
        int championId,
        string role,
        string? rankTier,
        string? region,
        string? patch,
        CancellationToken ct)
    {
        var patchContext = await ResolvePatchContextAsync(patch, ct);
        var normalizedRole = role.ToUpperInvariant();
        var normalizedTier = NormalizeRankTier(rankTier);
        var normalizedRegion = NormalizeAnalyticsRegion(region);

        if (string.IsNullOrWhiteSpace(patchContext.Patch))
            return new ChampionBuildsResponse(
                championId,
                normalizedRole,
                normalizedTier,
                normalizedRegion,
                "Unknown",
                [],
                [],
                BuildSampleMetadata(0, patchContext));

        var selectedPatch = patchContext.Patch!;

        var cacheKey = $"{BuildsCacheKeyPrefix}{championId}:{normalizedRole}:{normalizedTier}:{normalizedRegion}:{selectedPatch}";
        var tags = new[] { AnalyticsCacheTag, $"patch:{selectedPatch}", "builds" };

        var response = await _cache.GetOrCreateAsync(
            cacheKey,
            async cancel => await _computeService.ComputeBuildsAsync(
                championId,
                normalizedRole,
                normalizedTier == "all" ? null : normalizedTier,
                normalizedRegion,
                selectedPatch,
                cancel),
            AnalyticsCacheOptions,
            tags,
            cancellationToken: ct);
        var sampleSize = response.Builds.Sum(x => Math.Max(0, x.Games));
        return response with { Sample = BuildSampleMetadata(sampleSize, patchContext) };
    }

    public async Task<ChampionProBuildsResponse> GetProBuildsAsync(
        int championId,
        string? region,
        string? role,
        string? scope,
        string? patch,
        CancellationToken ct)
    {
        var patchContext = await ResolvePatchContextAsync(patch, ct);
        var resolvedPatch = patchContext.Patch;
        var normalizedRole = string.IsNullOrWhiteSpace(role) ? "ALL" : role.Trim().ToUpperInvariant();
        var normalizedRegion = string.IsNullOrWhiteSpace(region) ? "ALL" : region.Trim().ToUpperInvariant();
        var normalizedScope = ChampionAnalyticsComputeService.NormalizeProScope(scope);

        if (string.IsNullOrWhiteSpace(resolvedPatch))
            return new ChampionProBuildsResponse(
                championId,
                "Unknown",
                normalizedRole,
                normalizedRegion,
                normalizedScope,
                [],
                [],
                [],
                BuildSampleMetadata(0, patchContext));

        var cacheKey = $"{ProBuildsCacheKeyPrefix}{championId}:{normalizedRegion}:{normalizedRole}:{normalizedScope}:{resolvedPatch}";
        var tags = new[] { AnalyticsCacheTag, $"patch:{resolvedPatch}", "probuilds" };

        var response = await _cache.GetOrCreateAsync(
            cacheKey,
            async cancel => await _computeService.ComputeProBuildsAsync(
                championId,
                normalizedRegion,
                normalizedRole,
                normalizedScope,
                resolvedPatch,
                cancel),
            AnalyticsCacheOptions,
            tags,
            cancellationToken: ct);
        var sampleSize = Math.Max(
            response.CommonBuilds.Sum(x => Math.Max(0, x.Games)),
            response.RecentProMatches.Count);
        return response with { Sample = BuildSampleMetadata(sampleSize, patchContext) };
    }

    public async Task<ProChampionPlayrateResponse> GetProChampionPlayrateAsync(
        string? region,
        string? scope,
        string? patch,
        CancellationToken ct)
    {
        var patchContext = await ResolvePatchContextAsync(patch, ct);
        var normalizedRegion = string.IsNullOrWhiteSpace(region) ? "ALL" : region.Trim().ToUpperInvariant();
        var normalizedScope = ChampionAnalyticsComputeService.NormalizeProScope(scope);

        if (string.IsNullOrWhiteSpace(patchContext.Patch))
            return new ProChampionPlayrateResponse(
                "Unknown",
                normalizedRegion,
                normalizedScope,
                [],
                BuildSampleMetadata(0, patchContext));

        var resolvedPatch = patchContext.Patch!;
        var cacheKey = $"{ProPlayrateCacheKeyPrefix}{normalizedScope}:{normalizedRegion}:{resolvedPatch}";
        var tags = new[] { AnalyticsCacheTag, $"patch:{resolvedPatch}", "proplayrate" };

        var response = await _cache.GetOrCreateAsync(
            cacheKey,
            async cancel => await _computeService.ComputeProChampionPlayrateAsync(
                normalizedRegion,
                normalizedScope,
                resolvedPatch,
                cancel),
            AnalyticsCacheOptions,
            tags,
            cancellationToken: ct);
        var sampleSize = response.Champions.Sum(x => Math.Max(0, x.Games));
        return response with { Sample = BuildSampleMetadata(sampleSize, patchContext) };
    }

    public async Task<ProRosterResponse> GetProRosterAsync(
        string? region,
        CancellationToken ct)
    {
        var normalizedRegion = string.IsNullOrWhiteSpace(region) ? "ALL" : region.Trim().ToUpperInvariant();
        var cacheKey = $"{ProRosterCacheKeyPrefix}{normalizedRegion}";
        var tags = new[] { AnalyticsCacheTag, "proroster" };

        var players = await _cache.GetOrCreateAsync(
            cacheKey,
            async cancel => await _computeService.ComputeProRosterAsync(normalizedRegion, cancel),
            AnalyticsCacheOptions,
            tags,
            cancellationToken: ct);

        return new ProRosterResponse(normalizedRegion, players);
    }

    public async Task<ChampionMatchupsResponse> GetMatchupsAsync(
        int championId,
        string role,
        string? rankTier,
        string? region,
        string? patch,
        CancellationToken ct)
    {
        var patchContext = await ResolvePatchContextAsync(patch, ct);
        var normalizedRole = role.ToUpperInvariant();
        var normalizedTier = NormalizeRankTier(rankTier);
        var normalizedRegion = NormalizeAnalyticsRegion(region);

        if (string.IsNullOrWhiteSpace(patchContext.Patch))
        {
            return new ChampionMatchupsResponse
            {
                ChampionId = championId,
                Role = normalizedRole,
                RankTier = normalizedTier,
                Region = normalizedRegion,
                Patch = "Unknown",
                Counters = [],
                FavorableMatchups = [],
                AllMatchups = [],
                Sample = BuildSampleMetadata(0, patchContext)
            };
        }

        var selectedPatch = patchContext.Patch!;
        var cacheKey = $"{MatchupsCacheKeyPrefix}{championId}:{normalizedRole}:{normalizedTier}:{normalizedRegion}:{selectedPatch}";
        var tags = new[] { AnalyticsCacheTag, $"patch:{selectedPatch}", "matchups" };

        var response = await _cache.GetOrCreateAsync(
            cacheKey,
            async cancel => await _computeService.ComputeMatchupsAsync(
                championId,
                normalizedRole,
                normalizedTier == "all" ? null : normalizedTier,
                normalizedRegion,
                selectedPatch,
                cancel),
            AnalyticsCacheOptions,
            tags,
            cancellationToken: ct);
        var sampleSize = response.AllMatchups.Sum(x => Math.Max(0, x.Games));
        return response with { Sample = BuildSampleMetadata(sampleSize, patchContext) };
    }

    public async Task InvalidateAnalyticsCacheAsync(CancellationToken ct)
    {
        await _cache.RemoveByTagAsync(AnalyticsCacheTag, ct);
    }

    public async Task InvalidateAnalyticsCacheForPatchAsync(string patch, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(patch))
        {
            await InvalidateAnalyticsCacheAsync(ct);
            return;
        }

        await _cache.RemoveByTagAsync($"patch:{patch.Trim()}", ct);
    }

    public async Task<string?> RefreshDefaultProfileCacheAsync(
        int championId,
        string? rankTier,
        bool includeProBuilds,
        CancellationToken ct)
    {
        var patchContext = await ResolvePatchContextAsync(null, ct);
        var currentPatch = patchContext.Patch;
        if (string.IsNullOrWhiteSpace(currentPatch))
            return null;

        var normalizedTier = NormalizeRankTier(rankTier);
        var tierForCompute = normalizedTier == "all" ? null : normalizedTier;
        var normalizedRegion = NormalizeAnalyticsRegion(null); // "ALL"
        var globalRegionFilter = AnalyticsRegionCatalog.NormalizeToFilter(normalizedRegion);

        // ── Win rates: mirror the profile endpoint's default read (given tier, region=ALL, no role).
        // Compute fresh, then SetAsync the SAME key/tags GetWinRatesAsync would cache under so the
        // page read is a hit. SetAsync overwrites in place — no invalidate-then-cold gap.
        var winFilter = new ChampionAnalyticsFilter(
            RankTier: tierForCompute,
            Region: globalRegionFilter,
            Role: null,
            Patch: currentPatch);
        var winKey = BuildCacheKey(championId, winFilter, currentPatch);
        var winTags = new[] { AnalyticsCacheTag, $"champion:{championId}", $"patch:{currentPatch}" };
        var winRates = await _computeService.ComputeWinRatesAsync(championId, winFilter, currentPatch, ct);
        await _cache.SetAsync(winKey, winRates, AnalyticsCacheOptions, winTags, ct);

        // Resolve the most-played lane EXACTLY like ChampionAnalyticsController.GetProfile so the
        // builds/matchups keys we warm match what the page will request.
        var effectiveRole = PickMostPlayedLane(winRates);
        if (effectiveRole == null && normalizedTier != "all")
        {
            // Mirror GetProfile's all-rank fallback: when the scoped tier has no rows, resolve the
            // lane from all-rank win rates (builds/matchups still warmed at the scoped tier).
            var fallbackFilter = new ChampionAnalyticsFilter(
                RankTier: null,
                Region: globalRegionFilter,
                Role: null,
                Patch: currentPatch);
            var fallbackKey = BuildCacheKey(championId, fallbackFilter, currentPatch);
            var fallbackWinRates = await _computeService.ComputeWinRatesAsync(championId, fallbackFilter, currentPatch, ct);
            await _cache.SetAsync(fallbackKey, fallbackWinRates, AnalyticsCacheOptions, winTags, ct);
            effectiveRole = PickMostPlayedLane(fallbackWinRates);
        }

        effectiveRole ??= "MIDDLE"; // mirror GetProfile's final fallback so the key always matches

        // ── Builds + matchups for the resolved lane (region=ALL, given tier) ──
        var buildsKey = $"{BuildsCacheKeyPrefix}{championId}:{effectiveRole}:{normalizedTier}:{normalizedRegion}:{currentPatch}";
        var buildsTags = new[] { AnalyticsCacheTag, $"patch:{currentPatch}", "builds" };
        var builds = await _computeService.ComputeBuildsAsync(
            championId, effectiveRole, tierForCompute, normalizedRegion, currentPatch, ct);
        await _cache.SetAsync(buildsKey, builds, AnalyticsCacheOptions, buildsTags, ct);

        var matchupsKey = $"{MatchupsCacheKeyPrefix}{championId}:{effectiveRole}:{normalizedTier}:{normalizedRegion}:{currentPatch}";
        var matchupsTags = new[] { AnalyticsCacheTag, $"patch:{currentPatch}", "matchups" };
        var matchups = await _computeService.ComputeMatchupsAsync(
            championId, effectiveRole, tierForCompute, normalizedRegion, currentPatch, ct);
        await _cache.SetAsync(matchupsKey, matchups, AnalyticsCacheOptions, matchupsTags, ct);

        // ── Pro-builds default (most-played lane, region=ALL, scope=all) ──
        if (includeProBuilds)
        {
            var proScope = ChampionAnalyticsComputeService.NormalizeProScope(null); // "all"
            const string proRegion = "ALL";
            var proKey = $"{ProBuildsCacheKeyPrefix}{championId}:{proRegion}:{effectiveRole}:{proScope}:{currentPatch}";
            var proTags = new[] { AnalyticsCacheTag, $"patch:{currentPatch}", "probuilds" };
            var proBuilds = await _computeService.ComputeProBuildsAsync(
                championId, proRegion, effectiveRole, proScope, currentPatch, ct);
            await _cache.SetAsync(proKey, proBuilds, AnalyticsCacheOptions, proTags, ct);
        }

        return effectiveRole;
    }

    private async Task<ActivePatchContext> ResolvePatchContextAsync(string? requestedPatch, CancellationToken ct)
    {
        var normalizedRequestedPatch = string.IsNullOrWhiteSpace(requestedPatch)
            ? null
            : requestedPatch.Trim();

        if (!string.IsNullOrWhiteSpace(normalizedRequestedPatch))
        {
            var requestedPatchMetadata = await _context.Patches
                .AsNoTracking()
                .Where(p => p.Version == normalizedRequestedPatch)
                .Select(p => new { p.Version, p.ReleaseDate })
                .FirstOrDefaultAsync(ct);

            if (requestedPatchMetadata == null)
                return new ActivePatchContext(
                    normalizedRequestedPatch,
                    0,
                    false,
                    AnalyticsPatchPhase.Steady);

            return BuildPatchContext(requestedPatchMetadata.Version, requestedPatchMetadata.ReleaseDate);
        }

        var activePatch = await _cache.GetOrCreateAsync(
            ActivePatchCacheKey,
            async cancel =>
            {
                var row = await _context.Patches
                    .AsNoTracking()
                    .Where(p => p.IsActive)
                    .Select(p => new { p.Version, p.ReleaseDate })
                    .FirstOrDefaultAsync(cancel);
                return row == null ? null : new ActivePatchInfo(row.Version, row.ReleaseDate);
            },
            ActivePatchCacheOptions,
            tags: new[] { AnalyticsCacheTag },
            cancellationToken: ct);

        if (activePatch == null || string.IsNullOrWhiteSpace(activePatch.Version))
            return new ActivePatchContext(null, 0, false, AnalyticsPatchPhase.Bootstrap);

        return BuildPatchContext(activePatch.Version, activePatch.ReleaseDate);
    }

    private ActivePatchContext BuildPatchContext(string patch, DateTime releaseDate)
    {
        var releaseUtc = releaseDate.Kind == DateTimeKind.Utc
            ? releaseDate
            : DateTime.SpecifyKind(releaseDate, DateTimeKind.Utc);
        var patchAgeHours = Math.Max(0, (DateTime.UtcNow - releaseUtc).TotalHours);
        var patchPhase = AnalyticsPatchPhaseCalculator.Resolve(patchAgeHours, _computeOptions);
        return new ActivePatchContext(
            patch,
            patchAgeHours,
            patchPhase != AnalyticsPatchPhase.Steady,
            patchPhase);
    }

    private static string BuildCacheKey(int championId, ChampionAnalyticsFilter filter, string patch)
    {
        var keyParts = new List<string>
        {
            $"{WinRateCacheKeyPrefix}{championId}",
            $"patch:{patch}"
        };

        if (!string.IsNullOrEmpty(filter.RankTier))
            keyParts.Add($"tier:{filter.RankTier}");

        if (!string.IsNullOrEmpty(filter.Region))
            keyParts.Add($"region:{filter.Region}");

        if (!string.IsNullOrEmpty(filter.Role))
            keyParts.Add($"role:{filter.Role}");

        return string.Join(":", keyParts);
    }

    private string NormalizeAnalyticsRegion(string? region)
    {
        var normalized = AnalyticsRegionCatalog.NormalizeOrDefault(region);
        var allowed = AnalyticsRegionCatalog.BuildAvailableRegions(_multiRegionOptions)
            .Select(x => x.Code)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return allowed.Contains(normalized)
            ? normalized
            : AnalyticsRegionCatalog.GlobalRegionCode;
    }

    private static readonly HashSet<string> LaneRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "TOP", "JUNGLE", "MIDDLE", "BOTTOM", "UTILITY"
    };

    // Must produce the SAME result as ChampionAnalyticsController.PickMostPlayedRole (operating on
    // the by-role win-rate rows) so the lane this warms matches the lane the profile page requests.
    private static string? PickMostPlayedLane(IEnumerable<ChampionWinRateDto> winRates) =>
        winRates
            .Select(row => new { Role = NormalizeLane(row.Role), row.Games })
            .Where(row => row.Role != null)
            .GroupBy(row => row.Role!)
            .Select(group => new { Role = group.Key, Games = group.Sum(row => Math.Max(0, row.Games)) })
            .OrderByDescending(row => row.Games)
            .Select(row => row.Role)
            .FirstOrDefault();

    private static string? NormalizeLane(string? role)
    {
        if (string.IsNullOrWhiteSpace(role))
            return null;

        var upper = role.Trim().ToUpperInvariant();
        return LaneRoles.Contains(upper) ? upper : null;
    }

    private static string NormalizeRankTier(string? rankTier)
    {
        if (string.IsNullOrWhiteSpace(rankTier))
            return "all";

        var normalized = rankTier.Trim().ToUpperInvariant().Replace("+", "_PLUS");
        return normalized == "ALL" ? "all" : normalized;
    }

    private AnalyticsSampleMetadata BuildSampleMetadata(int sampleSize, ActivePatchContext patchContext)
    {
        var minimumRecommended = AnalyticsPatchPhaseCalculator.RecommendedSampleSize(
            patchContext.PatchPhase,
            _computeOptions);
        var status = sampleSize <= 0
            ? AnalyticsSampleStatus.NoData
            : sampleSize < minimumRecommended
                ? AnalyticsSampleStatus.LowSample
                : AnalyticsSampleStatus.Sufficient;

        return new AnalyticsSampleMetadata(
            status,
            sampleSize,
            minimumRecommended,
            Math.Round(patchContext.PatchAgeHours, 1),
            patchContext.IsEarlyPatchWindow,
            patchContext.PatchPhase,
            patchContext.PatchPhase != AnalyticsPatchPhase.Steady);
    }

    private sealed record ActivePatchContext(
        string? Patch,
        double PatchAgeHours,
        bool IsEarlyPatchWindow,
        AnalyticsPatchPhase PatchPhase);
}
