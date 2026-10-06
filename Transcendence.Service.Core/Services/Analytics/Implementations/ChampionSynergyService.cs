using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Transcendence.Data;
using Transcendence.Data.Models.LoL.Analytics;
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

    // Durable default snapshots survive process/L2 cold starts. Compact facts retain current-rank
    // semantics after source archiving; incomplete patches use the raw fallback. Patch refreshes of
    // unrelated tables must not invalidate these six-hour entries (only the analytics tag does).
    private static readonly HybridCacheEntryOptions CacheOptions = new()
    {
        Expiration = TimeSpan.FromHours(6),
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

        var key = CacheKey(championId, normalizedRole, rankScope.CacheToken, normalizedRegion, normalizedQueue, patch);
        return await cache.GetOrCreateAsync(
            key,
            async cancel =>
            {
                var oldest = DateTime.UtcNow.AddHours(-24);
                var payload = await context.AnalyticsResponseSnapshots.AsNoTracking()
                    .Where(row => row.Feature == "synergies" && row.ScopeKey == key && row.Patch == patch &&
                                  row.ComputedAtUtc >= oldest)
                    .Select(row => row.Payload).FirstOrDefaultAsync(cancel);
                if (payload != null && JsonSerializer.Deserialize<ChampionSynergiesResponse>(payload) is { } stored)
                    return stored;
                return await ComputeAsync(championId, normalizedRole, rankScope, normalizedRegion,
                    normalizedQueue, patch, cancel);
            },
            CacheOptions,
            tags: ["analytics"],
            cancellationToken: ct);
    }

    private static string CacheKey(int champion, string role, string tier, string region, string queue, string patch) =>
        $"analytics:synergies:v2:{champion}:{role}:{tier}:{region}:{queue}:{patch}";

    public async Task RefreshSnapshotAsync(int championId, string role, string rankTier, string patch, CancellationToken ct = default)
    {
        var scope = AnalyticsScopeMath.ParseRankTierScope(rankTier);
        var key = CacheKey(championId, role, scope.CacheToken, "ALL", QueueCatalog.QueueFamilyRankedSoloDuo, patch);
        var snapshot = await context.AnalyticsResponseSnapshots.FirstOrDefaultAsync(row =>
            row.Feature == "synergies" && row.ScopeKey == key && row.Patch == patch, ct);
        if (snapshot != null && snapshot.ComputedAtUtc > DateTime.UtcNow.AddHours(-6))
            return;
        var result = await ComputeAsync(championId, role, scope, "ALL", QueueCatalog.QueueFamilyRankedSoloDuo, patch, ct);
        if (snapshot == null)
        {
            snapshot = new AnalyticsResponseSnapshot
            {
                Id = Guid.NewGuid(), Feature = "synergies", ScopeKey = key, Patch = patch
            };
            context.AnalyticsResponseSnapshots.Add(snapshot);
        }
        snapshot.Payload = JsonSerializer.Serialize(result);
        snapshot.ComputedAtUtc = DateTime.UtcNow;
        await context.SaveChangesAsync(ct);
        await cache.SetAsync(key, result, CacheOptions, tags: ["analytics"], cancellationToken: ct);
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
        if (await context.AnalyticsResponseSnapshots.AsNoTracking().AnyAsync(row =>
                row.Feature == ChampionSynergyFactMaterializer.CoverageFeature && row.ScopeKey == "global" && row.Patch == patch, ct))
            return await ComputeFromFactsAsync(championId, role, rankScope, region, queueFamily, patch, ct);

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
                // ParticipantId, not Id: within one match it names the same participant, and unlike Id
                // it is in both covering indexes, so neither side of the pair needs a heap fetch
                // (Id cost a random heap read per partner row, 150K disk reads an hour on prod).
                pair.Focal.ParticipantId != pair.Partner.ParticipantId &&
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

        return BuildResponse(championId, role, rankScope.CacheToken, region, queueFamily, patch, baseline, pairRows);
    }

    private async Task<ChampionSynergiesResponse> ComputeFromFactsAsync(
        int championId, string role, AnalyticsScopeMath.RankTierScope rankScope,
        string region, string queueFamily, string patch, CancellationToken ct)
    {
        var query = context.ChampionSynergyFacts.AsNoTracking().Where(fact =>
            fact.Patch == patch && fact.QueueFamily == queueFamily && fact.ChampionId == championId && fact.Role == role);
        var platform = AnalyticsRegionCatalog.NormalizeToFilter(region);
        if (platform != null)
            query = query.Where(fact => fact.PlatformRegion == platform);
        if (rankScope.HasFilter)
        {
            var ranks = context.Ranks.AsNoTracking().InAnalyticsRankQueue(queueFamily);
            query = rankScope.IsEmeraldPlus
                ? query.Where(fact => ranks.Any(rank => rank.SummonerId == fact.SummonerId && RankTierCatalog.EmeraldPlusTiers.Contains(rank.Tier)))
                : query.Where(fact => ranks.Any(rank => rank.SummonerId == fact.SummonerId && rank.Tier == rankScope.ExactTier));
        }
        var baseline = await query.Where(fact => fact.PartnerParticipantId == 0).GroupBy(_ => 1)
            .Select(group => new BaselineRow { Games = group.Count(), Wins = group.Count(fact => fact.Win) })
            .FirstOrDefaultAsync(ct);
        if (baseline == null || baseline.Games == 0)
            return Empty(championId, role, rankScope.CacheToken, region, patch, queueFamily);
        var pairs = await query.Where(fact => fact.PartnerParticipantId != 0)
            .GroupBy(fact => new { fact.PartnerChampionId, fact.PartnerRole })
            .Select(group => new PairAggregateRow
            {
                PartnerChampionId = group.Key.PartnerChampionId, PartnerRole = group.Key.PartnerRole,
                Games = group.Count(), Wins = group.Count(fact => fact.Win)
            }).ToListAsync(ct);
        return BuildResponse(championId, role, rankScope.CacheToken, region, queueFamily, patch, baseline, pairs);
    }

    private static ChampionSynergiesResponse BuildResponse(
        int championId, string role, string rankToken, string region, string queueFamily, string patch,
        BaselineRow baseline, List<PairAggregateRow> pairRows)
    {
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
            rankToken,
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
