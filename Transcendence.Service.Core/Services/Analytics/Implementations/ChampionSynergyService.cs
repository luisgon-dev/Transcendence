using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Transcendence.Data;
using Transcendence.Service.Core.Queries;
using Transcendence.Service.Core.Services.Analytics.Interfaces;
using Transcendence.Service.Core.Services.Analytics.Models;
using Transcendence.Service.Core.Services.Cache;
using Transcendence.Service.Core.Services.RiotApi;

namespace Transcendence.Service.Core.Services.Analytics.Implementations;

/// <summary>
/// Same-team champion-pair analytics for the role pairings players can act on: bot carry/support
/// and jungle/lane. Pair win rate is compared with the focal champion's scoped baseline and ranked
/// by a Wilson lower-bound delta so tiny, lucky samples do not become the first recommendation.
/// </summary>
public sealed class ChampionSynergyService(
    TranscendenceContext context,
    HybridCache cache,
    IAnalyticsPatchQueryService patchQueryService) : IChampionSynergyService
{
    private const int ConfiguredMinimumPairGames = 30;
    private const int PartnersToShow = 10;

    private static readonly HybridCacheEntryOptions CacheOptions = new()
    {
        Expiration = TimeSpan.FromHours(24),
        LocalCacheExpiration = TimeSpan.FromHours(1)
    };

    public async Task<ChampionSynergiesResponse> GetSynergiesAsync(
        int championId,
        string role,
        string? rankTier,
        string? region,
        string? queueFamily,
        string? requestedPatch,
        CancellationToken ct = default)
    {
        var normalizedRole = role.Trim().ToUpperInvariant();
        var normalizedQueue = AnalyticsQueueCatalog.Normalize(queueFamily);
        var normalizedRegion = AnalyticsRegionCatalog.NormalizeOrDefault(region);
        var rankScope = AnalyticsScopeMath.ParseRankTierScope(rankTier);
        var patch = await ResolvePatchAsync(requestedPatch, normalizedQueue, ct);
        if (string.IsNullOrEmpty(patch) || !AnalyticsQueueCatalog.HasRoles(normalizedQueue))
            return Empty(championId, normalizedRole, rankScope.CacheToken, normalizedRegion, patch, normalizedQueue);

        var key = $"analytics:synergies:v1:{championId}:{normalizedRole}:{rankScope.CacheToken}:{normalizedRegion}:{normalizedQueue}:{patch}";
        return await cache.GetOrCreateAsync(
            key,
            cancel => ComputeAsync(
                championId,
                normalizedRole,
                rankScope,
                normalizedRegion,
                normalizedQueue,
                patch,
                cancel),
            CacheOptions,
            tags: ["analytics", CacheTags.ForPatch(patch)],
            cancellationToken: ct);
    }

    private async ValueTask<ChampionSynergiesResponse> ComputeAsync(
        int championId,
        string role,
        AnalyticsScopeMath.RankTierScope rankScope,
        string region,
        string queueFamily,
        string patch,
        CancellationToken ct)
    {
        var focalQuery = context.MatchParticipants
            .AsNoTracking()
            .Where(participant => participant.ChampionId == championId && participant.TeamPosition == role)
            .OnPatch(patch)
            .FromSuccessfulMatches()
            .InAnalyticsQueue(queueFamily)
            .InPlatformRegion(AnalyticsRegionCatalog.NormalizeToFilter(region));
        focalQuery = AnalyticsScopeMath.ApplyRankTierScopeToParticipants(
            focalQuery,
            rankScope,
            context.Ranks.AsNoTracking(),
            queueFamily);

        var baseline = await focalQuery
            .GroupBy(_ => 1)
            .Select(group => new BaselineRow
            {
                Games = group.Count(),
                Wins = group.Count(participant => participant.Win)
            })
            .FirstOrDefaultAsync(ct);
        if (baseline is null || baseline.Games == 0)
            return Empty(championId, role, rankScope.CacheToken, region, patch, queueFamily);

        var pairRows = await focalQuery
            .Join(
                context.MatchParticipants.AsNoTracking(),
                focal => focal.MatchId,
                partner => partner.MatchId,
                (focal, partner) => new { Focal = focal, Partner = partner })
            .Where(pair =>
                pair.Focal.TeamId == pair.Partner.TeamId &&
                pair.Focal.Id != pair.Partner.Id &&
                pair.Focal.ChampionId != pair.Partner.ChampionId)
            .Where(pair =>
                (role == "BOTTOM" && pair.Partner.TeamPosition == "UTILITY") ||
                (role == "UTILITY" && pair.Partner.TeamPosition == "BOTTOM") ||
                (role == "JUNGLE" && pair.Partner.TeamPosition != null &&
                    pair.Partner.TeamPosition != "" && pair.Partner.TeamPosition != "JUNGLE") ||
                ((role == "TOP" || role == "MIDDLE") && pair.Partner.TeamPosition == "JUNGLE"))
            .GroupBy(pair => new { pair.Partner.ChampionId, Role = pair.Partner.TeamPosition! })
            .Select(group => new PairAggregateRow
            {
                PartnerChampionId = group.Key.ChampionId,
                PartnerRole = group.Key.Role,
                Games = group.Count(),
                Wins = group.Count(pair => pair.Focal.Win)
            })
            .ToListAsync(ct);

        var baselineWinRate = (double)baseline.Wins / baseline.Games;
        var minimumGames = AnalyticsScopeMath.ResolveEffectiveSampleSize(
            ConfiguredMinimumPairGames,
            baseline.Games,
            floor: 3);
        var partners = pairRows
            .Where(pair => pair.Games >= minimumGames)
            .Select(pair =>
            {
                var winRate = (double)pair.Wins / pair.Games;
                var confidenceScore = AnalyticsScopeMath.ComputeWilsonLowerBound(pair.Wins, pair.Games) - baselineWinRate;
                return new ChampionSynergyEntryDto(
                    pair.PartnerChampionId,
                    pair.PartnerRole,
                    pair.Games,
                    pair.Wins,
                    winRate,
                    (double)pair.Games / baseline.Games,
                    winRate - baselineWinRate,
                    confidenceScore);
            })
            .OrderByDescending(pair => pair.ConfidenceScore)
            .ThenByDescending(pair => pair.Games)
            .ThenBy(pair => pair.PartnerChampionId)
            .Take(PartnersToShow)
            .ToList();

        return new ChampionSynergiesResponse(
            championId,
            role,
            rankScope.CacheToken,
            region,
            patch,
            queueFamily,
            baseline.Games,
            baseline.Wins,
            baselineWinRate,
            partners);
    }

    private async Task<string> ResolvePatchAsync(string? requestedPatch, string queueFamily, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(requestedPatch))
            return requestedPatch.Trim();

        var options = await patchQueryService.GetPatchOptionsAsync(queueFamily, ct);
        return options.FirstOrDefault(option => option.IsActive && option.RankedSoloDuoMatchCount > 0)?.Patch
            ?? options.FirstOrDefault(option => option.RankedSoloDuoMatchCount > 0)?.Patch
            ?? string.Empty;
    }

    private static ChampionSynergiesResponse Empty(
        int championId,
        string role,
        string rankTier,
        string region,
        string patch,
        string queueFamily) =>
        new(championId, role, rankTier, region, patch, queueFamily, 0, 0, 0, []);

    private sealed class BaselineRow
    {
        public int Games { get; init; }
        public int Wins { get; init; }
    }

    private sealed class PairAggregateRow
    {
        public int PartnerChampionId { get; init; }
        public string PartnerRole { get; init; } = string.Empty;
        public int Games { get; init; }
        public int Wins { get; init; }
    }
}
