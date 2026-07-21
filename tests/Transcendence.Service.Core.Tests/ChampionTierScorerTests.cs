using FluentAssertions;
using Transcendence.Service.Core.Services.Analytics;
using Transcendence.Service.Core.Services.Analytics.Models;

namespace Transcendence.Service.Core.Tests;

/// <summary>
/// Pure (no-DB) unit tests for the single champion tier-grading core, <see cref="ChampionTierScorer"/>:
/// empirical-Bayes win-rate-delta strength, the method-of-moments Beta prior fit, the absolute-cutoff tier
/// mapping with its games-floor cap, per-role baselines, the separate contested index, and totality
/// (determinism + finite output on degenerate inputs). Requires assembly-internal access (InternalsVisibleTo).
/// </summary>
public class ChampionTierScorerTests
{
    private const int LargeScopeMatches = 100_000;

    private static ChampionTierScorer.ChampionAggregate Champ(
        int id, string role, int games, int wins, int banMatches = 0, int? scopeGamesAllRoles = null) =>
        new(id, role, games, wins, banMatches, scopeGamesAllRoles ?? games);

    [Fact]
    public void ScoreRole_HighSample_SplitsStrengthSymmetricallyAndGradesSAndD()
    {
        // DEFAULT options. A MIDDLE role of two well-sampled champs: 54% vs 46% over 10k games each.
        var population = new[]
        {
            Champ(1, "MIDDLE", games: 10_000, wins: 5_400),
            Champ(2, "MIDDLE", games: 10_000, wins: 4_600),
        };

        var scored = ChampionTierScorer.ScoreRole(population, totalRoleGames: 20_000, LargeScopeMatches, new TieringOptions());

        scored.Should().HaveCount(2);

        // Baseline mu = 10000/20000 = 0.5; Beta moments give k = m(1-m)/v - 1 = 0.25/0.0016 - 1 = 155.25.
        var top = scored[0];
        top.ChampionId.Should().Be(1);
        top.RoleBaseline.Should().BeApproximately(0.5, 1e-12);
        top.PriorStrength.Should().BeApproximately(155.25, 1e-9);
        top.IsLowSample.Should().BeFalse();          // broad role volume resolves to the 500-game ceiling
        top.WinRate.Should().BeApproximately(0.54, 1e-12);
        top.PickRate.Should().BeApproximately(0.5, 1e-12);
        // strength = (0.54 - 0.5) * 10000 / (10000 + 155.25) = 400 / 10155.25 ≈ +0.0393885.
        top.StrengthScore.Should().BeApproximately(0.0393885, 1e-5);
        top.Tier.Should().Be(TierGrade.S);           // >= SMin (0.03)

        var bottom = scored[1];
        bottom.ChampionId.Should().Be(2);
        bottom.IsLowSample.Should().BeFalse();
        bottom.StrengthScore.Should().BeApproximately(-0.0393885, 1e-5);
        bottom.Tier.Should().Be(TierGrade.D);        // < CMin (-0.03)
    }

    [Fact]
    public void ScoreRole_TinySample_ShrinksToBaselineAndGradesB()
    {
        // 3 wins / 4 games against a balanced baseline: heavy shrinkage toward mu AND below the games floor.
        var population = new[]
        {
            Champ(1, "TOP", games: 4, wins: 3),
            Champ(2, "TOP", games: 4, wins: 1),
        };

        var scored = ChampionTierScorer.ScoreRole(population, totalRoleGames: 8, LargeScopeMatches, new TieringOptions());

        var champ1 = scored.Single(c => c.ChampionId == 1);
        champ1.WinRate.Should().BeApproximately(0.75, 1e-12);
        // mu = 0.5, k = PriorStrengthMax (2000, no champ meets the adaptive prior-fit floor):
        // strength = 0.25*4/2004 ≈ 0.0005.
        champ1.StrengthScore.Should().BeInRange(-0.001, 0.001);
        champ1.IsLowSample.Should().BeTrue();
        champ1.Tier.Should().Be(TierGrade.B);        // both shrunk to ~0 delta AND low-sample
    }

    [Fact]
    public void ScoreRole_ClearsTopCutoffButBelowGamesFloor_IsCappedToBAndFlaggedLowSample()
    {
        // Custom options make a 100-game champ's shrunk delta clear SMin, isolating the games-floor cap.
        var options = new TieringOptions
        {
            PriorStrengthMin = 1,
            PriorStrengthMax = 10,
            PriorFitMinGamesFloor = 10,
            PriorFitMinGamesCeiling = 10,
            GradeMinGamesFloor = 500,
            GradeMinGamesCeiling = 500
        };

        var population = new[]
        {
            Champ(1, "TOP", games: 100, wins: 70),   // 70% vs a 50% baseline
            Champ(2, "TOP", games: 100, wins: 30),
        };

        var scored = ChampionTierScorer.ScoreRole(population, totalRoleGames: 200, LargeScopeMatches, options);

        var champ1 = scored.Single(c => c.ChampionId == 1);
        // k = 0.25/0.04 - 1 = 5.25; strength = (0.7-0.5)*100/105.25 ≈ 0.19 — comfortably above SMin (0.03).
        champ1.StrengthScore.Should().BeGreaterThan(options.Cutoffs.SMin);
        champ1.IsLowSample.Should().BeTrue();        // 100 < fixed 500-game test floor
        champ1.Tier.Should().Be(TierGrade.B);        // would be S, but a thin sample is capped at B
    }

    [Fact]
    public void ResolveAdaptiveThreshold_LiveCalibratedDefaultsScaleBetweenThinAndBroadScopes()
    {
        var options = new TieringOptions();

        ChampionTierScorer.ResolveAdaptiveThreshold(
                totalRoleGames: 2_000,
                options.GradeRoleVolumeShare,
                options.GradeMinGamesFloor,
                options.GradeMinGamesCeiling)
            .Should().Be(50);
        ChampionTierScorer.ResolveAdaptiveThreshold(
                totalRoleGames: 50_000,
                options.GradeRoleVolumeShare,
                options.GradeMinGamesFloor,
                options.GradeMinGamesCeiling)
            .Should().Be(150);
        ChampionTierScorer.ResolveAdaptiveThreshold(
                totalRoleGames: 200_000,
                options.GradeRoleVolumeShare,
                options.GradeMinGamesFloor,
                options.GradeMinGamesCeiling)
            .Should().Be(500);

        ChampionTierScorer.ResolveAdaptiveThreshold(
                totalRoleGames: 2_000,
                options.PriorFitRoleVolumeShare,
                options.PriorFitMinGamesFloor,
                options.PriorFitMinGamesCeiling)
            .Should().Be(20);
        ChampionTierScorer.ResolveAdaptiveThreshold(
                totalRoleGames: 200_000,
                options.PriorFitRoleVolumeShare,
                options.PriorFitMinGamesFloor,
                options.PriorFitMinGamesCeiling)
            .Should().Be(200);
    }

    [Fact]
    public void ScoreRole_ThinScopeCanResolveSignalAboveTheAdaptiveFloor()
    {
        var population = new[]
        {
            Champ(1, "TOP", games: 100, wins: 70),
            Champ(2, "TOP", games: 100, wins: 30),
        };

        var scored = ChampionTierScorer.ScoreRole(
            population,
            totalRoleGames: 200,
            LargeScopeMatches,
            new TieringOptions());

        var strongest = scored.Single(champion => champion.ChampionId == 1);
        strongest.IsLowSample.Should().BeFalse();
        strongest.Tier.Should().Be(TierGrade.S);
    }

    [Fact]
    public void ScoreRole_BalancedPopulation_LeavesTheSTierEmpty()
    {
        // Two well-sampled exactly-50% champs: zero variance → maximal shrinkage → no real edge → no S.
        var population = new[]
        {
            Champ(1, "MIDDLE", games: 1_000, wins: 500),
            Champ(2, "MIDDLE", games: 1_000, wins: 500),
        };

        var scored = ChampionTierScorer.ScoreRole(population, totalRoleGames: 2_000, LargeScopeMatches, new TieringOptions());

        scored.Should().OnlyContain(c => c.IsLowSample == false);
        scored.Should().OnlyContain(c => c.Tier == TierGrade.B);
        scored.Should().NotContain(c => c.Tier == TierGrade.S); // S can legitimately be empty on a balanced patch
    }

    [Fact]
    public void ScoreRole_AbsoluteCutoffs_MapStrengthDeltasToSABCDAtTheBoundaries()
    {
        // Pin the prior strength to a fixed k=1 and use huge samples so posterior ≈ win rate (shrink ≈ 1),
        // then place champions just above/below the ±0.03 and ±0.015 cutoffs around an exact 0.5 baseline.
        var options = new TieringOptions { PriorStrengthMin = 1, PriorStrengthMax = 1 };
        const int g = 1_000_000;

        // (id, wins, expected tier) — symmetric around 0.5 so the baseline is exactly 0.5.
        var population = new[]
        {
            Champ(1, "TOP", g, 530_100), // +0.0301 -> S
            Champ(2, "TOP", g, 529_900), // +0.0299 -> A
            Champ(3, "TOP", g, 516_000), // +0.016  -> A
            Champ(4, "TOP", g, 514_000), // +0.014  -> B
            Champ(5, "TOP", g, 486_000), // -0.014  -> B
            Champ(6, "TOP", g, 484_000), // -0.016  -> C
            Champ(7, "TOP", g, 470_100), // -0.0299 -> C
            Champ(8, "TOP", g, 469_900), // -0.0301 -> D
        };

        var scored = ChampionTierScorer.ScoreRole(population, totalRoleGames: g * population.Length, LargeScopeMatches, options);

        TierOf(scored, 1).Should().Be(TierGrade.S);
        TierOf(scored, 2).Should().Be(TierGrade.A);
        TierOf(scored, 3).Should().Be(TierGrade.A);
        TierOf(scored, 4).Should().Be(TierGrade.B);
        TierOf(scored, 5).Should().Be(TierGrade.B);
        TierOf(scored, 6).Should().Be(TierGrade.C);
        TierOf(scored, 7).Should().Be(TierGrade.C);
        TierOf(scored, 8).Should().Be(TierGrade.D);

        scored.Single(c => c.ChampionId == 1).RoleBaseline.Should().BeApproximately(0.5, 1e-9);

        static TierGrade TierOf(IReadOnlyList<ChampionTierScorer.ScoredChampion> s, int id) =>
            s.Single(c => c.ChampionId == id).Tier;
    }

    [Fact]
    public void ScoreRole_StrengthIsRelativeToTheRoleBaseline_NotTheRawWinRate()
    {
        // Fixed k=1, huge samples: an identical raw 50% champ swings sign with the role baseline.
        var options = new TieringOptions { PriorStrengthMin = 1, PriorStrengthMax = 1 };
        const int g = 1_000_000;

        // Role X centered ~0.48: a 50% champ sits ABOVE baseline.
        var roleX = ChampionTierScorer.ScoreRole(
            new[] { Champ(1, "TOP", g, 500_000), Champ(2, "TOP", g, 460_000) },
            totalRoleGames: 2 * g, LargeScopeMatches, options);

        // Role Y centered ~0.52: the same 50% champ sits BELOW baseline.
        var roleY = ChampionTierScorer.ScoreRole(
            new[] { Champ(1, "MIDDLE", g, 500_000), Champ(2, "MIDDLE", g, 540_000) },
            totalRoleGames: 2 * g, LargeScopeMatches, options);

        var inX = roleX.Single(c => c.ChampionId == 1);
        var inY = roleY.Single(c => c.ChampionId == 1);

        inX.RoleBaseline.Should().BeApproximately(0.48, 1e-9);
        inY.RoleBaseline.Should().BeApproximately(0.52, 1e-9);

        inX.WinRate.Should().BeApproximately(0.5, 1e-12);
        inY.WinRate.Should().BeApproximately(0.5, 1e-12);

        inX.StrengthScore.Should().BeApproximately(0.02, 1e-4);   // +(0.50 - 0.48)
        inY.StrengthScore.Should().BeApproximately(-0.02, 1e-4);  // -(0.52 - 0.50)
        Math.Sign(inX.StrengthScore).Should().Be(-Math.Sign(inY.StrengthScore));
    }

    [Fact]
    public void ScoreRole_IsOrderIndependent_ShufflingInputProducesIdenticalOutput()
    {
        var population = new[]
        {
            Champ(7, "TOP", games: 1_200, wins: 700),
            Champ(3, "TOP", games: 800, wins: 360),
            Champ(11, "TOP", games: 2_000, wins: 1_010),
            Champ(2, "TOP", games: 450, wins: 250),
            Champ(5, "TOP", games: 1_500, wins: 690),
        };

        var baseline = ChampionTierScorer.ScoreRole(population, totalRoleGames: 5_950, LargeScopeMatches, new TieringOptions());

        var reversed = population.Reverse().ToArray();
        var permuted = new[] { population[3], population[0], population[4], population[1], population[2] };

        ChampionTierScorer.ScoreRole(reversed, 5_950, LargeScopeMatches, new TieringOptions())
            .Should().Equal(baseline);
        ChampionTierScorer.ScoreRole(permuted, 5_950, LargeScopeMatches, new TieringOptions())
            .Should().Equal(baseline);
    }

    [Fact]
    public void ScoreRole_DegenerateInputs_ProduceFiniteBGradesWithNoNaNOrInfinity()
    {
        var single = ChampionTierScorer.ScoreRole(
            new[] { Champ(1, "TOP", games: 100, wins: 60) }, totalRoleGames: 100, LargeScopeMatches, new TieringOptions());
        single.Should().ContainSingle();
        AssertFiniteBaselineB(single);

        var allEqual = ChampionTierScorer.ScoreRole(
            new[]
            {
                Champ(1, "TOP", games: 1_000, wins: 500),
                Champ(2, "TOP", games: 1_000, wins: 500),
                Champ(3, "TOP", games: 1_000, wins: 500),
            },
            totalRoleGames: 3_000, LargeScopeMatches, new TieringOptions());
        allEqual.Should().HaveCount(3);
        AssertFiniteBaselineB(allEqual);

        static void AssertFiniteBaselineB(IReadOnlyList<ChampionTierScorer.ScoredChampion> scored)
        {
            foreach (var c in scored)
            {
                double.IsNaN(c.StrengthScore).Should().BeFalse();
                double.IsInfinity(c.StrengthScore).Should().BeFalse();
                double.IsNaN(c.PriorStrength).Should().BeFalse();
                c.StrengthScore.Should().BeApproximately(0.0, 1e-9); // collapses to the baseline
                c.Tier.Should().Be(TierGrade.B);
            }
        }
    }

    [Fact]
    public void ScoreRole_ContestedIndex_IsTheWeightedPresencePlusBanRate()
    {
        var population = new[]
        {
            Champ(1, "TOP", games: 100, wins: 50, banMatches: 50, scopeGamesAllRoles: 300),
        };

        // Default weights (1, 1): contested = 1*(300/1000) + 1*(50/1000) = 0.35; ban rate = 50/1000 = 0.05.
        var defaultWeighted = ChampionTierScorer.ScoreRole(population, totalRoleGames: 100, totalScopeMatches: 1_000, new TieringOptions());
        var d = defaultWeighted.Single();
        d.BanRate.Should().BeApproximately(0.05, 1e-12);
        d.ContestedScore.Should().BeApproximately(0.35, 1e-12);

        // Custom weights: contested = 2*0.3 + 0.5*0.05 = 0.625 (strength axis is untouched by pick/ban).
        var customWeighted = ChampionTierScorer.ScoreRole(
            population, totalRoleGames: 100, totalScopeMatches: 1_000,
            new TieringOptions { ContestPickWeight = 2.0, ContestBanWeight = 0.5 });
        customWeighted.Single().ContestedScore.Should().BeApproximately(0.625, 1e-12);
    }

    [Fact]
    public void ScoreScope_Overview_PicksEachChampionsMostPlayedRoleAndKeepsPresenceCrossRole()
    {
        // champ 1: TOP (100g) + MIDDLE (50g); champ 2: MIDDLE (80g). No bans. 1000 distinct scope matches.
        var aggregated = new[]
        {
            new ChampionTierScorer.RoleGames(1, "TOP", 100, 55),
            new ChampionTierScorer.RoleGames(1, "MIDDLE", 50, 25),
            new ChampionTierScorer.RoleGames(2, "MIDDLE", 80, 40),
        };

        var score = ChampionTierScorer.ScoreScope(
            aggregated, new Dictionary<int, int>(), totalScopeMatches: 1_000, new TieringOptions());

        // Per-role: one row per (champion, role) played.
        score.PerRole.Should().HaveCount(3);

        // Overview: one row per champion at its most-played role.
        score.Overview.Should().HaveCount(2);
        score.Overview.Single(c => c.ChampionId == 1).Role.Should().Be("TOP");    // 100 > 50
        score.Overview.Single(c => c.ChampionId == 2).Role.Should().Be("MIDDLE");

        // Contested presence is the champion's cross-role games over scope matches: champ 1 = (100+50)/1000.
        var champ1Top = score.PerRole.Single(c => c.ChampionId == 1 && c.Role == "TOP");
        champ1Top.ContestedScore.Should().BeApproximately(0.15, 1e-12);
    }
}
