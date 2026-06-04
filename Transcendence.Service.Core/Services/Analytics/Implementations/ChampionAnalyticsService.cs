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
    private const string BuildsCacheKeyPrefix = "analytics:builds:";
    private const string ProBuildsCacheKeyPrefix = "analytics:probuilds:";
    private const string ProPlayrateCacheKeyPrefix = "analytics:proplayrate:";
    private const string ProRosterCacheKeyPrefix = "analytics:proroster:";
    private const string MatchupsCacheKeyPrefix = "analytics:matchups:";
    private const string AnalyticsCacheTag = "analytics";

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
        string? patch,
        CancellationToken ct)
    {
        var patchContext = await ResolvePatchContextAsync(patch, ct);
        var resolvedPatch = patchContext.Patch;
        var normalizedRole = string.IsNullOrWhiteSpace(role) ? "ALL" : role.Trim().ToUpperInvariant();
        var normalizedRegion = string.IsNullOrWhiteSpace(region) ? "ALL" : region.Trim().ToUpperInvariant();

        if (string.IsNullOrWhiteSpace(resolvedPatch))
            return new ChampionProBuildsResponse(
                championId,
                "Unknown",
                normalizedRole,
                normalizedRegion,
                [],
                [],
                [],
                BuildSampleMetadata(0, patchContext));

        var cacheKey = $"{ProBuildsCacheKeyPrefix}{championId}:{normalizedRegion}:{normalizedRole}:{resolvedPatch}";
        var tags = new[] { AnalyticsCacheTag, $"patch:{resolvedPatch}", "probuilds" };

        var response = await _cache.GetOrCreateAsync(
            cacheKey,
            async cancel => await _computeService.ComputeProBuildsAsync(
                championId,
                normalizedRegion,
                normalizedRole,
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

        var activePatch = await _context.Patches
            .AsNoTracking()
            .Where(p => p.IsActive)
            .Select(p => new { p.Version, p.ReleaseDate })
            .FirstOrDefaultAsync(ct);

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
