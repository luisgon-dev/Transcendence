using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Options;
using Transcendence.Data;
using Transcendence.Service.Core.Services.Analytics.Interfaces;
using Transcendence.Service.Core.Services.Analytics.Models;
using Transcendence.Service.Core.Services.Analytics;
using Transcendence.Service.Core.Services.Cache;
using Transcendence.Service.Core.Services.Jobs.Configuration;
using Transcendence.Service.Core.Services.RiotApi;

namespace Transcendence.Service.Core.Services.Analytics.Implementations;

/// <summary>
/// Cached analytics service for champion win rates and statistics.
/// Uses HybridCache for 24-hour L2 and 1-hour L1 caching.
/// </summary>
public class ChampionAnalyticsService : IChampionAnalyticsService
{
    private const string WinRateCacheKeyPrefix = "analytics:champion:winrates:";
    // v3: per-role-first empirical-Bayes tiering (strength delta + absolute cutoffs + new entry fields).
    // Bumped so stale v2 percentile-composite payloads are not served from cache.
    private const string TierListCacheKeyPrefix = "analytics:tierlist:v4:";
    // v2: ordered, timing-aware sectioned builds (Build Analysis Overhaul). Bumped so stale
    // pre-overhaul payloads are not served from cache.
    private const string BuildsCacheKeyPrefix = "analytics:builds:v2:";
    private const string ProBuildsCacheKeyPrefix = "analytics:probuilds:v2:";
    private const string ProPlayrateCacheKeyPrefix = "analytics:proplayrate:";
    private const string ProRosterCacheKeyPrefix = "analytics:proroster:";
    private const string MatchupsCacheKeyPrefix = "analytics:matchups:";
    private const string AnalyticsCacheTag = "analytics";

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
    private readonly IChampionMatchupComputeService _matchupService;
    private readonly IChampionWinRateComputeService _winRateService;
    private readonly IChampionBuildComputeService _buildService;
    private readonly IChampionProComputeService _proService;
    private readonly ChampionAnalyticsComputeOptions _computeOptions;
    private readonly MultiRegionIngestionOptions _multiRegionOptions;

    // Short TTL for freshly-computed empty / zero-sample payloads so incoming games surface within
    // minutes instead of inheriting the 24h analytics expiration (built per-instance from options).
    private readonly HybridCacheEntryOptions _emptyResultCacheOptions;

    public ChampionAnalyticsService(
        TranscendenceContext context,
        HybridCache cache,
        IChampionMatchupComputeService matchupService,
        IChampionWinRateComputeService winRateService,
        IChampionBuildComputeService buildService,
        IChampionProComputeService proService,
        IOptions<ChampionAnalyticsComputeOptions> computeOptions,
        IOptions<MultiRegionIngestionOptions> multiRegionOptions)
    {
        _context = context;
        _cache = cache;
        _matchupService = matchupService;
        _winRateService = winRateService;
        _buildService = buildService;
        _proService = proService;
        _computeOptions = computeOptions.Value;
        _multiRegionOptions = multiRegionOptions.Value;

        var emptyTtl = TimeSpan.FromMinutes(Math.Max(1, _computeOptions.EmptyResultTtlMinutes));
        _emptyResultCacheOptions = new HybridCacheEntryOptions
        {
            Expiration = emptyTtl,
            LocalCacheExpiration = emptyTtl < TimeSpan.FromMinutes(2) ? emptyTtl : TimeSpan.FromMinutes(2)
        };
    }

    /// <summary>
    /// When the precomputed analytics for <paramref name="patch"/> were last refreshed (the "updated N ago"
    /// signal), cached briefly per patch since it only advances on the hourly refresh. Null while a patch is
    /// still served by live compute (no aggregates yet).
    /// </summary>
    private async Task<DateTime?> ResolveAnalyticsFreshnessAsync(
        string patch,
        string queueFamily,
        CancellationToken ct) =>
        await _cache.GetOrCreateAsync(
            $"analytics:freshness:v2:{queueFamily}:{patch}",
            async cancel => queueFamily == QueueCatalog.QueueFamilyRankedSoloDuo
                ? await _winRateService.GetAnalyticsComputedAtAsync(patch, cancel)
                : await _winRateService.GetAnalyticsComputedAtAsync(patch, queueFamily, cancel),
            ActivePatchCacheOptions,
            tags: new[] { AnalyticsCacheTag, CacheTags.ForPatch(patch) },
            cancellationToken: ct);

    /// <summary>
    /// Wraps <see cref="HybridCache.GetOrCreateAsync"/> so a freshly-computed empty / zero-sample
    /// payload is re-cached under a short TTL (<see cref="_emptyResultCacheOptions"/>) instead of
    /// inheriting the 24h analytics expiration. Newly-ingested games then surface within minutes
    /// rather than waiting out the patch-tag invalidation. Non-empty results and plain cache hits are
    /// returned untouched with the standard 24h TTL.
    /// </summary>
    private async Task<T> GetOrCreateWithEmptyTtlAsync<T>(
        string key,
        Func<CancellationToken, ValueTask<T>> factory,
        Func<T, bool> isEmpty,
        IEnumerable<string> tags,
        CancellationToken ct)
    {
        var computed = false;
        var value = await _cache.GetOrCreateAsync(
            key,
            async cancel =>
            {
                computed = true;
                return await factory(cancel);
            },
            AnalyticsCacheOptions,
            tags: tags,
            cancellationToken: ct);

        // Only shorten the TTL when WE just computed the value (a hit already carries the right TTL).
        if (computed && isEmpty(value))
        {
            await _cache.SetAsync(key, value, _emptyResultCacheOptions, tags, ct);
        }

        return value;
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
        var normalizedQueue = AnalyticsQueueCatalog.Normalize(filter.QueueFamily);
        var normalizedFilter = filter with
        {
            RankTier = normalizedRankTier == "all" ? null : normalizedRankTier,
            Region = AnalyticsRegionCatalog.NormalizeToFilter(NormalizeAnalyticsRegion(filter.Region)),
            Role = AnalyticsQueueCatalog.HasRoles(normalizedQueue) && !string.IsNullOrWhiteSpace(filter.Role)
                ? filter.Role.Trim().ToUpperInvariant()
                : null,
            QueueFamily = normalizedQueue,
            Patch = currentPatch
        };

        // Build cache key based on normalized filter parameters
        var cacheKey = BuildWinRateKey(championId, normalizedFilter, currentPatch);

        // Serve from the precomputed aggregate tables (fast indexed scope roll-up; falls back to the raw
        // compute for any patch without aggregates yet). HybridCache stays the hot tier in front.
        var winRates = await GetOrCreateWithEmptyTtlAsync(
            cacheKey,
            async cancel => await _winRateService.ComputeWinRatesFromStatsAsync(championId, normalizedFilter, currentPatch, cancel),
            static list => list.Sum(x => Math.Max(0, x.Games)) <= 0,
            new[] { AnalyticsCacheTag, $"champion:{championId}", CacheTags.ForPatch(currentPatch) },
            ct
        );

        var sampleSize = winRates.Sum(x => Math.Max(0, x.Games));
        return new ChampionWinRateSummary(
            ChampionId: championId,
            Patch: currentPatch,
            ByRoleTier: winRates,
            Sample: BuildSampleMetadata(sampleSize, patchContext),
            ComputedAtUtc: await ResolveAnalyticsFreshnessAsync(currentPatch, normalizedQueue, ct),
            QueueFamily: normalizedQueue
        );
    }

    public async Task<TierListResponse> GetTierListAsync(
        string? role,
        string? rankTier,
        string? region,
        string? patch,
        CancellationToken ct) =>
        await GetTierListAsync(role, rankTier, region, null, patch, ct);

    public async Task<TierListResponse> GetTierListAsync(
        string? role,
        string? rankTier,
        string? region,
        string? queueFamily,
        string? patch,
        CancellationToken ct)
    {
        var patchContext = await ResolvePatchContextAsync(patch, ct);
        var normalizedRegion = NormalizeAnalyticsRegion(region);
        var normalizedQueue = AnalyticsQueueCatalog.Normalize(queueFamily);
        if (string.IsNullOrWhiteSpace(patchContext.Patch))
        {
            return new TierListResponse(
                Patch: "Unknown",
                Role: role,
                RankTier: rankTier,
                Region: normalizedRegion,
                Entries: new List<TierListEntry>(),
                Sample: BuildSampleMetadata(0, patchContext),
                QueueFamily: normalizedQueue,
                Confidence: TierScopeConfidence.INSUFFICIENT
            );
        }

        var currentPatch = patchContext.Patch!;
        // Normalize parameters
        var normalizedRole = string.IsNullOrEmpty(role) ? "ALL" : role.ToUpperInvariant();
        if (!AnalyticsQueueCatalog.HasRoles(normalizedQueue))
            normalizedRole = AnalyticsQueueCatalog.AllRoles;
        var normalizedTier = NormalizeRankTier(rankTier);
        var tierFilter = normalizedTier == "all" ? null : normalizedTier;

        // Build cache key
        var cacheKey = $"{TierListCacheKeyPrefix}{normalizedQueue}:{normalizedRole}:{normalizedTier}:{normalizedRegion}:{currentPatch}";
        var tags = new[] { AnalyticsCacheTag, CacheTags.ForPatch(currentPatch), "tierlist" };

        // Serve from the precomputed aggregate tables (falls back to the raw compute until a patch's
        // aggregates exist). HybridCache stays the hot tier in front.
        var entries = await _cache.GetOrCreateAsync(
            cacheKey,
            async cancel => normalizedQueue == QueueCatalog.QueueFamilyRankedSoloDuo
                ? await _winRateService.ComputeTierListFromStatsAsync(
                    normalizedRole, tierFilter, normalizedRegion, currentPatch, cancel)
                : await _winRateService.ComputeTierListFromStatsAsync(
                    normalizedRole, tierFilter, normalizedRegion, normalizedQueue, currentPatch, cancel),
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
            Sample: BuildSampleMetadata(sampleSize, patchContext),
            ComputedAtUtc: await ResolveAnalyticsFreshnessAsync(currentPatch, normalizedQueue, ct),
            QueueFamily: normalizedQueue,
            Confidence: DeriveTierScopeConfidence(entries)
        );
    }

    private static TierScopeConfidence DeriveTierScopeConfidence(IReadOnlyList<TierListEntry> entries)
    {
        if (entries.Count == 0 || entries.All(entry => entry.IsLowSample))
            return TierScopeConfidence.INSUFFICIENT;

        var firstTier = entries[0].Tier;
        return entries.All(entry => entry.Tier == firstTier)
            ? TierScopeConfidence.FLAT
            : TierScopeConfidence.RESOLVED;
    }

    public async Task<ChampionGradeDto?> GetGradeAsync(
        int championId,
        string role,
        string? rankTier,
        string? region,
        string? patch,
        CancellationToken ct) =>
        await GetGradeAsync(
            championId, role, rankTier, region, QueueCatalog.QueueFamilyRankedSoloDuo, patch, ct);

    public async Task<ChampionGradeDto?> GetGradeAsync(
        int championId,
        string role,
        string? rankTier,
        string? region,
        string? queueFamily,
        string? patch,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(role))
            return null;

        // The grade IS the champion's entry in the per-role tier list — reuse its cache so the detail-page
        // hero is guaranteed to match the list (and carries the persisted region=ALL movement).
        var tierList = await GetTierListAsync(role, rankTier, region, queueFamily, patch, ct);
        var entry = tierList.Entries.FirstOrDefault(e => e.ChampionId == championId);
        if (entry == null)
            return null;

        return new ChampionGradeDto(
            Tier: entry.Tier,
            StrengthScore: entry.StrengthScore,
            WinRate: entry.WinRate,
            PickRate: entry.PickRate,
            BanRate: entry.BanRate,
            ContestedScore: entry.ContestedScore,
            Games: entry.Games,
            RoleBaseline: entry.RoleBaseline,
            IsLowSample: entry.IsLowSample,
            Movement: entry.Movement,
            PreviousTier: entry.PreviousTier,
            Role: entry.Role,
            RankScope: tierList.RankTier ?? "all");
    }

    public async Task<ChampionBuildsResponse> GetBuildsAsync(
        int championId,
        string role,
        string? rankTier,
        string? region,
        string? patch,
        CancellationToken ct) =>
        await GetBuildsAsync(
            championId, role, rankTier, region, QueueCatalog.QueueFamilyRankedSoloDuo, patch, ct);

    public async Task<ChampionBuildsResponse> GetBuildsAsync(
        int championId,
        string role,
        string? rankTier,
        string? region,
        string? queueFamily,
        string? patch,
        CancellationToken ct)
    {
        var normalizedQueue = AnalyticsQueueCatalog.Normalize(queueFamily);
        var patchContext = await ResolvePatchContextAsync(patch, ct);
        var normalizedRole = AnalyticsQueueCatalog.HasRoles(normalizedQueue)
            ? role.ToUpperInvariant()
            : AnalyticsQueueCatalog.AllRoles;
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
                BuildSampleMetadata(0, patchContext),
                QueueFamily: normalizedQueue);

        var selectedPatch = patchContext.Patch!;

        var cacheKey = BuildBuildsKey(
            championId, normalizedRole, normalizedTier, normalizedRegion, normalizedQueue, selectedPatch);
        var tags = new[] { AnalyticsCacheTag, CacheTags.ForPatch(selectedPatch), "builds" };

        var response = await _cache.GetOrCreateAsync(
            cacheKey,
            async cancel => await _buildService.ComputeBuildsFromStatsAsync(
                championId,
                normalizedRole,
                normalizedTier == "all" ? null : normalizedTier,
                normalizedRegion,
                normalizedQueue,
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
        var normalizedScope = ChampionProComputeService.NormalizeProScope(scope);

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

        var cacheKey = BuildProBuildsKey(championId, normalizedRegion, normalizedRole, normalizedScope, resolvedPatch);
        var tags = new[] { AnalyticsCacheTag, CacheTags.ForPatch(resolvedPatch), "probuilds" };

        var response = await _cache.GetOrCreateAsync(
            cacheKey,
            async cancel => await _proService.ComputeProBuildsFromStatsAsync(
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
        var normalizedScope = ChampionProComputeService.NormalizeProScope(scope);

        if (string.IsNullOrWhiteSpace(patchContext.Patch))
            return new ProChampionPlayrateResponse(
                "Unknown",
                normalizedRegion,
                normalizedScope,
                [],
                BuildSampleMetadata(0, patchContext));

        var resolvedPatch = patchContext.Patch!;
        var cacheKey = $"{ProPlayrateCacheKeyPrefix}{normalizedScope}:{normalizedRegion}:{resolvedPatch}";
        var tags = new[] { AnalyticsCacheTag, CacheTags.ForPatch(resolvedPatch), "proplayrate" };

        var response = await _cache.GetOrCreateAsync(
            cacheKey,
            async cancel => await _proService.ComputeProChampionPlayrateFromStatsAsync(
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
            async cancel => await _proService.ComputeProRosterAsync(normalizedRegion, cancel),
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
        CancellationToken ct) =>
        await GetMatchupsAsync(
            championId, role, rankTier, region, QueueCatalog.QueueFamilyRankedSoloDuo, patch, ct);

    public async Task<ChampionMatchupsResponse> GetMatchupsAsync(
        int championId,
        string role,
        string? rankTier,
        string? region,
        string? queueFamily,
        string? patch,
        CancellationToken ct)
    {
        var normalizedQueue = AnalyticsQueueCatalog.Normalize(queueFamily);
        var patchContext = await ResolvePatchContextAsync(patch, ct);
        var normalizedRole = AnalyticsQueueCatalog.HasRoles(normalizedQueue)
            ? role.ToUpperInvariant()
            : AnalyticsQueueCatalog.AllRoles;
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
                Sample = BuildSampleMetadata(0, patchContext),
                QueueFamily = normalizedQueue
            };
        }

        var selectedPatch = patchContext.Patch!;
        var cacheKey = BuildMatchupsKey(
            championId, normalizedRole, normalizedTier, normalizedRegion, normalizedQueue, selectedPatch);
        var tags = new[] { AnalyticsCacheTag, CacheTags.ForPatch(selectedPatch), "matchups" };

        // Serve from the precomputed matchup aggregates (all-region scope; falls back to the raw self-join
        // for a specific region or an un-refreshed patch). HybridCache stays the hot tier in front.
        var response = await _cache.GetOrCreateAsync(
            cacheKey,
            async cancel => await _matchupService.ComputeMatchupsFromStatsAsync(
                championId,
                normalizedRole,
                normalizedTier == "all" ? null : normalizedTier,
                normalizedRegion,
                normalizedQueue,
                selectedPatch,
                cancel),
            AnalyticsCacheOptions,
            tags,
            cancellationToken: ct);
        var sampleSize = response.AllMatchups.Sum(x => Math.Max(0, x.Games));
        return response with { Sample = BuildSampleMetadata(sampleSize, patchContext) };
    }

    public async Task<ChampionTrendResponse> GetTrendAsync(
        int championId,
        string? role,
        string? rankTier,
        string? queueFamily,
        CancellationToken ct)
    {
        var normalizedQueue = AnalyticsQueueCatalog.Normalize(queueFamily);
        var normalizedRole = AnalyticsQueueCatalog.HasRoles(normalizedQueue) &&
                             !string.IsNullOrWhiteSpace(role) &&
                             !string.Equals(role, AnalyticsQueueCatalog.AllRoles, StringComparison.OrdinalIgnoreCase)
            ? role.Trim().ToUpperInvariant()
            : AnalyticsQueueCatalog.AllRoles;
        var rankScope = AnalyticsScopeMath.ScopeTokenOf(AnalyticsScopeMath.ParseRankTierScope(rankTier));

        var rows = await (
                from grade in _context.ChampionScopeGradeStats.AsNoTracking()
                join patch in _context.Patches.AsNoTracking() on grade.Patch equals patch.Version
                where grade.ChampionId == championId &&
                      grade.QueueFamily == normalizedQueue &&
                      grade.PlatformRegion == AnalyticsRegionCatalog.GlobalRegionCode &&
                      grade.RankScope == rankScope &&
                      grade.Role == normalizedRole
                orderby patch.ReleaseDate descending, patch.Version descending
                select new
                {
                    grade.Patch,
                    patch.ReleaseDate,
                    grade.Tier,
                    grade.Games,
                    grade.WinRate,
                    grade.PickRate,
                    grade.BanRate,
                    grade.StrengthScore,
                    grade.IsLowSample
                })
            .Take(12)
            .ToListAsync(ct);

        var points = rows
            .OrderBy(row => row.ReleaseDate)
            .ThenBy(row => row.Patch)
            .Select(row => new ChampionTrendPointDto(
                row.Patch,
                row.ReleaseDate,
                (TierGrade)row.Tier,
                row.Games,
                row.WinRate,
                row.PickRate,
                row.BanRate,
                row.StrengthScore,
                row.IsLowSample))
            .ToList();

        return new ChampionTrendResponse(
            championId,
            normalizedQueue,
            normalizedRole,
            rankScope,
            AnalyticsRegionCatalog.GlobalRegionCode,
            points);
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

        await _cache.RemoveByTagAsync(CacheTags.ForPatch(patch), ct);
    }

    public async Task InvalidateProAnalyticsCacheAsync(CancellationToken ct)
    {
        await _cache.RemoveByTagAsync("proplayrate", ct);
        await _cache.RemoveByTagAsync("probuilds", ct);
        await _cache.RemoveByTagAsync("proroster", ct);
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
        var winKey = BuildWinRateKey(championId, winFilter, currentPatch);
        var winTags = new[] { AnalyticsCacheTag, $"champion:{championId}", CacheTags.ForPatch(currentPatch) };
        var winRates = await _winRateService.ComputeWinRatesFromStatsAsync(championId, winFilter, currentPatch, ct);
        await _cache.SetAsync(winKey, winRates, AnalyticsCacheOptions, winTags, ct);

        // Resolve the most-played lane EXACTLY like ChampionAnalyticsController.GetProfile so the
        // builds/matchups keys we warm match what the page will request.
        var effectiveRole = ChampionRoleResolver.PickMostPlayed(winRates);
        if (effectiveRole == null && normalizedTier != "all")
        {
            // Mirror GetProfile's all-rank fallback: when the scoped tier has no rows, resolve the
            // lane from all-rank win rates (builds/matchups still warmed at the scoped tier).
            var fallbackFilter = new ChampionAnalyticsFilter(
                RankTier: null,
                Region: globalRegionFilter,
                Role: null,
                Patch: currentPatch);
            var fallbackKey = BuildWinRateKey(championId, fallbackFilter, currentPatch);
            var fallbackWinRates = await _winRateService.ComputeWinRatesFromStatsAsync(championId, fallbackFilter, currentPatch, ct);
            await _cache.SetAsync(fallbackKey, fallbackWinRates, AnalyticsCacheOptions, winTags, ct);
            effectiveRole = ChampionRoleResolver.PickMostPlayed(fallbackWinRates);
        }

        effectiveRole ??= "MIDDLE"; // mirror GetProfile's final fallback so the key always matches

        // ── Builds + matchups for the resolved lane (region=ALL, given tier) ──
        var buildsKey = BuildBuildsKey(
            championId, effectiveRole, normalizedTier, normalizedRegion,
            QueueCatalog.QueueFamilyRankedSoloDuo, currentPatch);
        var buildsTags = new[] { AnalyticsCacheTag, CacheTags.ForPatch(currentPatch), "builds" };
        var builds = await _buildService.ComputeBuildsFromStatsAsync(
            championId, effectiveRole, tierForCompute, normalizedRegion, currentPatch, ct);
        await _cache.SetAsync(buildsKey, builds, AnalyticsCacheOptions, buildsTags, ct);

        var matchupsKey = BuildMatchupsKey(
            championId, effectiveRole, normalizedTier, normalizedRegion,
            QueueCatalog.QueueFamilyRankedSoloDuo, currentPatch);
        var matchupsTags = new[] { AnalyticsCacheTag, CacheTags.ForPatch(currentPatch), "matchups" };
        var matchups = await _matchupService.ComputeMatchupsFromStatsAsync(
            championId, effectiveRole, tierForCompute, normalizedRegion, currentPatch, ct);
        await _cache.SetAsync(matchupsKey, matchups, AnalyticsCacheOptions, matchupsTags, ct);

        // ── Pro-builds default (most-played lane, region=ALL, scope=pro) ──
        if (includeProBuilds)
        {
            var proScope = ChampionProComputeService.NormalizeProScope(null); // "pro"
            const string proRegion = "ALL";
            var proKey = BuildProBuildsKey(championId, proRegion, effectiveRole, proScope, currentPatch);
            var proTags = new[] { AnalyticsCacheTag, CacheTags.ForPatch(currentPatch), "probuilds" };
            var proBuilds = await _proService.ComputeProBuildsFromStatsAsync(
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
            AnalyticsCacheKeys.ActivePatch,
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

    // Shared cache-key builders — the reader path (GetBuildsAsync/GetProBuildsAsync/GetMatchupsAsync)
    // and the warm writer (RefreshDefaultProfileCacheAsync) MUST produce byte-identical keys or the
    // warmed entry never gets hit. Keep these as the single source of truth.
    private static string BuildBuildsKey(
        int championId, string role, string tier, string region, string queueFamily, string patch)
        => $"{BuildsCacheKeyPrefix}{championId}:{role}:{tier}:{region}:{queueFamily}:{patch}";

    private static string BuildMatchupsKey(
        int championId, string role, string tier, string region, string queueFamily, string patch)
        => $"{MatchupsCacheKeyPrefix}{championId}:{role}:{tier}:{region}:{queueFamily}:{patch}";

    private static string BuildProBuildsKey(int championId, string region, string role, string scope, string patch)
        => $"{ProBuildsCacheKeyPrefix}{championId}:{region}:{role}:{scope}:{patch}";

    private static string BuildWinRateKey(int championId, ChampionAnalyticsFilter filter, string patch)
    {
        var keyParts = new List<string>
        {
            $"{WinRateCacheKeyPrefix}{championId}",
            CacheTags.ForPatch(patch)
        };

        if (!string.IsNullOrEmpty(filter.RankTier))
            keyParts.Add($"tier:{filter.RankTier}");

        // Compute queries use null for the global filter, while cache identity always uses the
        // canonical region code. This keeps every analytics key on one representation ("ALL").
        keyParts.Add($"region:{AnalyticsRegionCatalog.NormalizeOrDefault(filter.Region)}");

        if (!string.IsNullOrEmpty(filter.Role))
            keyParts.Add($"role:{filter.Role}");

        keyParts.Add($"queue:{AnalyticsQueueCatalog.Normalize(filter.QueueFamily)}");

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
