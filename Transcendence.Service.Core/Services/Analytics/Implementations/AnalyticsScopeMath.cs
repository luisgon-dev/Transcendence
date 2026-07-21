using Microsoft.EntityFrameworkCore;
using Transcendence.Service.Core.Queries;
using Transcendence.Service.Core.Services.RiotApi;

namespace Transcendence.Service.Core.Services.Analytics.Implementations;

/// <summary>
/// Shared, stateless rank-tier-scope parsing/application + sample-size math for champion analytics.
/// Extracted from the original analytics compute service (P10.1) so the raw and stats-backed read paths
/// across <see cref="ChampionWinRateComputeService"/>, <see cref="ChampionBuildComputeService"/>,
/// <see cref="ChampionProComputeService"/>, and <see cref="ChampionMatchupComputeService"/> share one
/// focused, pure implementation. All members are pure functions of their parameters.
/// </summary>
internal static class AnalyticsScopeMath
{
    internal readonly record struct RankTierScope(
        string CacheToken,
        string? ExactTier,
        bool IsEmeraldPlus)
    {
        public bool HasFilter => IsEmeraldPlus || !string.IsNullOrWhiteSpace(ExactTier);
    }

    internal static RankTierScope ParseRankTierScope(string? rankTier)
    {
        if (string.IsNullOrWhiteSpace(rankTier))
            return new RankTierScope("all", null, false);

        var normalized = rankTier.Trim().ToUpperInvariant().Replace("+", "_PLUS");
        if (normalized == "ALL")
            return new RankTierScope("all", null, false);

        if (normalized == "EMERALD_PLUS")
            return new RankTierScope("EMERALD_PLUS", null, true);

        return new RankTierScope(normalized, normalized, false);
    }

    internal static IQueryable<Data.Models.LoL.Match.MatchParticipant> ApplyRankTierScopeToParticipants(
        IQueryable<Data.Models.LoL.Match.MatchParticipant> query,
        RankTierScope scope,
        IQueryable<Data.Models.LoL.Account.Rank> ranks,
        string queueFamily = QueueCatalog.QueueFamilyRankedSoloDuo)
    {
        if (!scope.HasFilter)
            return query;

        ranks = ranks.InAnalyticsRankQueue(queueFamily);

        if (scope.IsEmeraldPlus)
        {
            return query.Where(mp => ranks.Any(r =>
                r.SummonerId == mp.SummonerId &&
                RankTierCatalog.EmeraldPlusTiers.Contains(r.Tier)));
        }

        return query.Where(mp => ranks.Any(r =>
            r.SummonerId == mp.SummonerId &&
            r.Tier == scope.ExactTier));
    }

    internal static HashSet<string> ResolvePlatformsForRegion(string region)
    {
        return region switch
        {
            "NA" => ["NA1"],
            "EUW" => ["EUW1"],
            "KR" => ["KR"],
            "CN" => ["CN1", "CN2"],
            "ALL" => [],
            _ => [region]
        };
    }

    internal static int ResolveEffectiveSampleSize(int configuredMinimum, int availableGames, int floor)
    {
        if (availableGames <= 0)
            return int.MaxValue;

        var safeConfiguredMinimum = Math.Max(1, configuredMinimum);
        var safeFloor = Math.Max(1, floor);
        var proportionalMinimum = (int)Math.Ceiling(availableGames * 0.15);
        var boundedFloor = Math.Min(availableGames, Math.Max(safeFloor, proportionalMinimum));
        return Math.Max(1, Math.Min(safeConfiguredMinimum, boundedFloor));
    }

    internal static double ComputeWilsonLowerBound(int wins, int games, double z = 1.96)
    {
        if (games <= 0)
            return 0.0;

        var p = (double)wins / games;
        var zSquared = z * z;
        var denominator = 1 + zSquared / games;
        var center = p + zSquared / (2 * games);
        var margin = z * Math.Sqrt((p * (1 - p) + zSquared / (4 * games)) / games);
        return Math.Max(0.0, (center - margin) / denominator);
    }

    /// <summary>The rank-scope token for the ban tables, derived from the parsed scope (see ParseRankTierScope).</summary>
    internal static string ScopeTokenOf(RankTierScope scope) =>
        scope.IsEmeraldPlus
            ? RankTierCatalog.EmeraldPlusScope
            : string.IsNullOrWhiteSpace(scope.ExactTier) ? RankTierCatalog.AllScope : scope.ExactTier!;
}
