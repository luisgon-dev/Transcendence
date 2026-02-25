using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Transcendence.Data;
using Transcendence.Data.Models.LoL.Match;
using Transcendence.Service.Core.Services.Analytics.Interfaces;
using Transcendence.Service.Core.Services.Analytics.Models;

namespace Transcendence.Service.Core.Services.Analytics.Implementations;

/// <summary>
/// Cached analytics service for champion win rates and statistics.
/// Uses HybridCache for 24-hour L2 and 1-hour L1 caching.
/// </summary>
public class ChampionAnalyticsService : IChampionAnalyticsService
{
    private const string WinRateCacheKeyPrefix = "analytics:champion:winrates:";
    private const string TierListCacheKeyPrefix = "analytics:tierlist:";
    private const string BuildsCacheKeyPrefix = "analytics:builds:";
    private const string ProBuildsCacheKeyPrefix = "analytics:probuilds:";
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

    public ChampionAnalyticsService(
        TranscendenceContext context,
        HybridCache cache,
        IChampionAnalyticsComputeService computeService)
    {
        _context = context;
        _cache = cache;
        _computeService = computeService;
    }

    public async Task<ChampionWinRateSummary> GetWinRatesAsync(
        int championId,
        ChampionAnalyticsFilter filter,
        CancellationToken ct)
    {
        var currentPatch = await GetCurrentPatchOrFallbackAsync(ct);
        if (string.IsNullOrWhiteSpace(currentPatch))
        {
            // No active patch, return empty summary
            return new ChampionWinRateSummary(
                ChampionId: championId,
                Patch: "Unknown",
                ByRoleTier: new List<ChampionWinRateDto>()
            );
        }

        // Build cache key based on filter parameters
        var cacheKey = BuildCacheKey(championId, filter, currentPatch);

        // Get or compute win rates with caching
        var winRates = await _cache.GetOrCreateAsync(
            cacheKey,
            async cancel => await _computeService.ComputeWinRatesAsync(championId, filter, currentPatch, cancel),
            AnalyticsCacheOptions,
            tags: new[] { AnalyticsCacheTag, $"champion:{championId}", $"patch:{currentPatch}" },
            cancellationToken: ct
        );

        return new ChampionWinRateSummary(
            ChampionId: championId,
            Patch: currentPatch,
            ByRoleTier: winRates
        );
    }

    public async Task<TierListResponse> GetTierListAsync(
        string? role,
        string? rankTier,
        CancellationToken ct)
    {
        var currentPatch = await GetCurrentPatchOrFallbackAsync(ct);
        if (string.IsNullOrWhiteSpace(currentPatch))
        {
            // No active patch, return empty tier list
            return new TierListResponse(
                Patch: "Unknown",
                Role: role,
                RankTier: rankTier,
                Entries: new List<TierListEntry>()
            );
        }

        // Normalize parameters
        var normalizedRole = string.IsNullOrEmpty(role) ? "ALL" : role.ToUpperInvariant();
        var normalizedTier = string.IsNullOrWhiteSpace(rankTier)
            ? "all"
            : rankTier.Trim().ToUpperInvariant();
        var tierFilter = normalizedTier == "ALL" ? null : normalizedTier;

        // Build cache key
        var cacheKey = $"{TierListCacheKeyPrefix}{normalizedRole}:{normalizedTier}:{currentPatch}";
        var tags = new[] { AnalyticsCacheTag, $"patch:{currentPatch}", "tierlist" };

        // Get or compute tier list with caching
        var entries = await _cache.GetOrCreateAsync(
            cacheKey,
            async cancel => await _computeService.ComputeTierListAsync(normalizedRole, tierFilter, currentPatch, cancel),
            AnalyticsCacheOptions,
            tags: tags,
            cancellationToken: ct
        );

        return new TierListResponse(
            Patch: currentPatch,
            Role: normalizedRole,
            RankTier: normalizedTier,
            Entries: entries
        );
    }

    public async Task<ChampionBuildsResponse> GetBuildsAsync(
        int championId,
        string role,
        string? rankTier,
        CancellationToken ct)
    {
        var patch = await GetCurrentPatchOrFallbackAsync(ct);
        var normalizedRole = role.ToUpperInvariant();
        var normalizedTier = NormalizeRankTier(rankTier);

        if (string.IsNullOrWhiteSpace(patch))
            return new ChampionBuildsResponse(championId, normalizedRole, normalizedTier, "Unknown", [], []);

        var cacheKey = $"{BuildsCacheKeyPrefix}{championId}:{normalizedRole}:{normalizedTier}:{patch}";
        var tags = new[] { AnalyticsCacheTag, $"patch:{patch}", "builds" };

        return await _cache.GetOrCreateAsync(
            cacheKey,
            async cancel => await _computeService.ComputeBuildsAsync(
                championId, normalizedRole, normalizedTier == "all" ? null : normalizedTier, patch, cancel),
            AnalyticsCacheOptions,
            tags,
            cancellationToken: ct);
    }

    public async Task<ChampionProBuildsResponse> GetProBuildsAsync(
        int championId,
        string? region,
        string? role,
        string? patch,
        CancellationToken ct)
    {
        var resolvedPatch = string.IsNullOrWhiteSpace(patch)
            ? await GetCurrentPatchOrFallbackAsync(ct)
            : patch.Trim();
        var normalizedRole = string.IsNullOrWhiteSpace(role) ? "ALL" : role.Trim().ToUpperInvariant();
        var normalizedRegion = string.IsNullOrWhiteSpace(region) ? "ALL" : region.Trim().ToUpperInvariant();

        if (string.IsNullOrWhiteSpace(resolvedPatch))
            return new ChampionProBuildsResponse(championId, "Unknown", normalizedRole, normalizedRegion, [], [], []);

        var cacheKey = $"{ProBuildsCacheKeyPrefix}{championId}:{normalizedRegion}:{normalizedRole}:{resolvedPatch}";
        var tags = new[] { AnalyticsCacheTag, $"patch:{resolvedPatch}", "probuilds" };

        return await _cache.GetOrCreateAsync(
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
    }

    public async Task<ChampionMatchupsResponse> GetMatchupsAsync(
        int championId,
        string role,
        string? rankTier,
        CancellationToken ct)
    {
        var patch = await GetCurrentPatchOrFallbackAsync(ct);
        var normalizedRole = role.ToUpperInvariant();
        var normalizedTier = NormalizeRankTier(rankTier);

        if (string.IsNullOrWhiteSpace(patch))
        {
            return new ChampionMatchupsResponse
            {
                ChampionId = championId,
                Role = normalizedRole,
                RankTier = normalizedTier,
                Patch = "Unknown",
                Counters = [],
                FavorableMatchups = [],
                AllMatchups = []
            };
        }

        var cacheKey = $"{MatchupsCacheKeyPrefix}{championId}:{normalizedRole}:{normalizedTier}:{patch}";
        var tags = new[] { AnalyticsCacheTag, $"patch:{patch}", "matchups" };

        return await _cache.GetOrCreateAsync(
            cacheKey,
            async cancel => await _computeService.ComputeMatchupsAsync(
                championId, normalizedRole, normalizedTier == "all" ? null : normalizedTier, patch, cancel),
            AnalyticsCacheOptions,
            tags,
            cancellationToken: ct);
    }

    public async Task InvalidateAnalyticsCacheAsync(CancellationToken ct)
    {
        await _cache.RemoveByTagAsync(AnalyticsCacheTag, ct);
    }

    private async Task<string?> GetCurrentPatchOrFallbackAsync(CancellationToken ct)
    {
        var activePatch = await _context.Patches
            .AsNoTracking()
            .Where(p => p.IsActive)
            .Select(p => p.Version)
            .FirstOrDefaultAsync(ct);

        if (!string.IsNullOrWhiteSpace(activePatch))
            return activePatch;

        return await _context.Matches
            .AsNoTracking()
            .Where(m => m.Status == FetchStatus.Success && m.Patch != null && m.Patch != "")
            .OrderByDescending(m => m.FetchedAt)
            .ThenByDescending(m => m.Id)
            .Select(m => m.Patch)
            .FirstOrDefaultAsync(ct);
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

    private static string NormalizeRankTier(string? rankTier)
    {
        if (string.IsNullOrWhiteSpace(rankTier))
            return "all";

        var normalized = rankTier.Trim().ToUpperInvariant();
        return normalized == "ALL" ? "all" : normalized;
    }
}
