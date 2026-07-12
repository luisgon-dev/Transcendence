using Transcendence.Service.Core.Services.Analytics.Models;

namespace Transcendence.Service.Core.Services.Analytics;

/// <summary>
/// The single, pure champion tier-grading core. Every surface that shows a champion tier (the tier list,
/// the champions grid, the champion detail hero) and every compute path (the raw live scan, the
/// precomputed-stats read, and the refresher that persists grades) funnels through here, so a champion
/// gets one grade everywhere and the raw-vs-stats equivalence test holds by construction.
///
/// <para>Methodology (per-role-first, "data is the hero"):</para>
/// <list type="number">
/// <item>Score each <c>(rankScope, role)</c> population independently — champions are compared only to
/// same-role peers.</item>
/// <item><b>Strength = win-rate delta vs the role baseline</b> with <b>empirical-Bayes shrinkage</b>: pull
/// each champion's win rate toward the role mean by a sample-weighted amount (a Beta prior fit per role
/// via method-of-moments), then take the signed deviation from the baseline. Low-sample champions shrink
/// to ~0 delta (role-average) instead of being buried or flying to the top on noise.</item>
/// <item>Tier from <b>absolute</b> cutoffs on that delta (config-driven), gated by a minimum-games floor —
/// so "S" means a real, sample-resolvable edge and an empty S tier honestly means "balanced patch".</item>
/// <item>Pick/ban are <b>not</b> in the strength score — they feed a separate "most contested" index.</item>
/// </list>
///
/// <para>All methods are total: they never throw and never emit NaN/Inf. This is load-bearing because the
/// refresher persists grades inside the atom-refresh transaction, so a scorer failure would abort the whole
/// analytics refresh.</para>
/// </summary>
internal static class ChampionTierScorer
{
    /// <summary>Per-<c>(champion, role)</c> input for one scope. <see cref="ScopeGamesAllRoles"/> is the
    /// champion's total games across <i>all</i> roles in the scope (presence numerator for the contested index).</summary>
    internal readonly record struct ChampionAggregate(
        int ChampionId,
        string Role,
        int Games,
        int Wins,
        int BanMatches,
        int ScopeGamesAllRoles);

    /// <summary>A graded champion row. <see cref="StrengthScore"/> is the signed win-rate delta vs the role baseline.</summary>
    internal readonly record struct ScoredChampion(
        int ChampionId,
        string Role,
        TierGrade Tier,
        double StrengthScore,
        double WinRate,
        double PickRate,
        double BanRate,
        double ContestedScore,
        int Games,
        int Wins,
        double RoleBaseline,
        double PriorStrength,
        bool IsLowSample);

    /// <summary>Per-<c>(champion, role)</c> games/wins for a scope — the raw aggregate every caller starts from.</summary>
    internal readonly record struct RoleGames(int ChampionId, string Role, int Games, int Wins);

    /// <summary>The full scored output for one scope: every per-role row plus the primary-role overview.</summary>
    internal sealed record ScopeScore(
        IReadOnlyList<ScoredChampion> PerRole,
        IReadOnlyList<ScoredChampion> Overview)
    {
        /// <summary>
        /// Coarse confidence in this scope's tier spread, derived purely from the already-graded overview rows
        /// (adds no state, changes no grade). It exists because the uncalibrated defaults (the S/A games floor
        /// and the prior-fit floor) make a thin scope collapse toward the role baseline and grade almost
        /// everything B — which looks identical to a genuinely balanced meta. This lets a caller tell the two
        /// apart and render an "insufficient data" hint instead of a misleadingly uniform tier list.
        /// </summary>
        public ScopeConfidence Confidence => DeriveConfidence(Overview);
    }

    /// <summary>Whole-scope confidence signal (see <see cref="ScopeScore.Confidence"/>). Not a grade — a hint.</summary>
    internal enum ScopeConfidence
    {
        /// <summary>At least one champion cleared the games floor and the scope shows a spread of tiers.</summary>
        Resolved = 0,

        /// <summary>Some champions cleared the floor, yet every graded champion landed on the same tier — the
        /// list carries no separating signal (a possibly-balanced patch, but treat as low-information).</summary>
        Flat = 1,

        /// <summary>No champion in the scope reached the S/A games floor (or the scope is empty): every grade is
        /// a pure shrinkage artifact, not a resolved edge. The strongest "not enough data" signal.</summary>
        Insufficient = 2,
    }

    /// <summary>Derives <see cref="ScopeConfidence"/> from graded rows. Total: never throws.</summary>
    private static ScopeConfidence DeriveConfidence(IReadOnlyList<ScoredChampion> rows)
    {
        if (rows.Count == 0)
            return ScopeConfidence.Insufficient;

        var anyAboveFloor = false;
        var allOneTier = true;
        var firstTier = rows[0].Tier;
        foreach (var r in rows)
        {
            if (!r.IsLowSample) anyAboveFloor = true;
            if (r.Tier != firstTier) allOneTier = false;
        }

        if (!anyAboveFloor)
            return ScopeConfidence.Insufficient;

        return allOneTier ? ScopeConfidence.Flat : ScopeConfidence.Resolved;
    }

    /// <summary>
    /// The single shared entry point. Takes the per-<c>(champion, role)</c> games/wins for a whole scope
    /// (every role, every champion), the champion-level ban numerators, and the scope's distinct-match
    /// denominator; derives the per-role pick denominators and the cross-role presence, scores each role,
    /// and builds the primary-role overview. The raw compute path, the precomputed-stats path, and the
    /// refresher all call this, so a champion's grade is identical everywhere.
    /// </summary>
    internal static ScopeScore ScoreScope(
        IReadOnlyList<RoleGames> aggregated,
        IReadOnlyDictionary<int, int> banByChampion,
        int totalScopeMatches,
        TieringOptions options)
    {
        if (aggregated.Count == 0)
            return new ScopeScore([], []);

        // Champion presence numerator for the contested index: total games across all roles in the scope.
        var champTotalGames = new Dictionary<int, int>();
        foreach (var a in aggregated)
            champTotalGames[a.ChampionId] = champTotalGames.GetValueOrDefault(a.ChampionId) + a.Games;

        var perRole = new List<ScoredChampion>(aggregated.Count);
        foreach (var roleGroup in aggregated.GroupBy(x => x.Role))
        {
            var totalRoleGames = roleGroup.Sum(x => x.Games);
            var population = roleGroup
                .Select(x => new ChampionAggregate(
                    x.ChampionId, x.Role, x.Games, x.Wins,
                    banByChampion.GetValueOrDefault(x.ChampionId),
                    champTotalGames.GetValueOrDefault(x.ChampionId)))
                .ToList();
            perRole.AddRange(ScoreRole(population, totalRoleGames, totalScopeMatches, options));
        }

        return new ScopeScore(perRole, ScoreOverview(perRole));
    }

    /// <summary>
    /// Grades one <c>(rankScope, role)</c> population. <paramref name="totalRoleGames"/> is the pick-rate
    /// denominator (Σ games over every champion in this role/scope); <paramref name="totalScopeMatches"/>
    /// is the distinct-match denominator for ban rate and contested presence. Deterministic and total.
    /// </summary>
    internal static IReadOnlyList<ScoredChampion> ScoreRole(
        IReadOnlyList<ChampionAggregate> population,
        int totalRoleGames,
        int totalScopeMatches,
        TieringOptions options)
    {
        if (population.Count == 0)
            return [];

        // Sort by ChampionId so the role baseline + Beta moments are bit-identical regardless of source
        // row order (the raw and stats paths feed differently-ordered rows into the same scorer).
        var ordered = population.OrderBy(p => p.ChampionId).ToList();

        long sumWins = 0, sumGames = 0;
        foreach (var c in ordered)
        {
            sumWins += c.Wins;
            sumGames += c.Games;
        }
        if (sumGames == 0)
            return [];

        var roleBaseline = (double)sumWins / sumGames;        // mu
        var priorStrength = FitPriorStrength(ordered, options); // k

        var results = new List<ScoredChampion>(ordered.Count);
        foreach (var c in ordered)
        {
            var winRate = c.Games > 0 ? (double)c.Wins / c.Games : 0.0;

            // Empirical-Bayes posterior mean toward the role baseline: Beta(alpha=mu*k, beta=(1-mu)*k).
            var posterior = (c.Wins + roleBaseline * priorStrength) / (c.Games + priorStrength);
            var strength = posterior - roleBaseline;
            if (double.IsNaN(strength) || double.IsInfinity(strength))
                strength = 0.0;

            var isLowSample = c.Games < options.GradeMinGamesFloor;
            var tier = ResolveTier(strength, options.Cutoffs, isLowSample);

            var pickRate = totalRoleGames > 0 ? (double)c.Games / totalRoleGames : 0.0;
            var banRate = totalScopeMatches > 0 ? (double)c.BanMatches / totalScopeMatches : 0.0;
            var presence = totalScopeMatches > 0 ? (double)c.ScopeGamesAllRoles / totalScopeMatches : 0.0;
            var contested = options.ContestPickWeight * presence + options.ContestBanWeight * banRate;

            results.Add(new ScoredChampion(
                c.ChampionId, c.Role, tier, strength, winRate, pickRate, banRate, contested,
                c.Games, c.Wins, roleBaseline, priorStrength, isLowSample));
        }

        results.Sort(CompareByStrength);
        return results;
    }

    /// <summary>
    /// Builds the "All Roles" overview from already-scored per-role rows: one row per champion at its
    /// <b>primary (most-played)</b> role, carrying that role's grade (and that role's name in <see cref="ScoredChampion.Role"/>).
    /// </summary>
    internal static IReadOnlyList<ScoredChampion> ScoreOverview(IEnumerable<ScoredChampion> perRoleScored)
    {
        var byChampion = new Dictionary<int, ScoredChampion>();
        foreach (var s in perRoleScored)
        {
            if (!byChampion.TryGetValue(s.ChampionId, out var existing)
                || s.Games > existing.Games
                || (s.Games == existing.Games && string.CompareOrdinal(s.Role, existing.Role) < 0))
            {
                byChampion[s.ChampionId] = s;
            }
        }

        var overview = byChampion.Values.ToList();
        overview.Sort(CompareByStrength);
        return overview;
    }

    private static int CompareByStrength(ScoredChampion a, ScoredChampion b)
    {
        var s = b.StrengthScore.CompareTo(a.StrengthScore);
        if (s != 0) return s;
        var g = b.Games.CompareTo(a.Games);
        if (g != 0) return g;
        return a.ChampionId.CompareTo(b.ChampionId);
    }

    private static TierGrade ResolveTier(double strength, TierCutoffs cutoffs, bool isLowSample)
    {
        var tier =
            strength >= cutoffs.SMin ? TierGrade.S :
            strength >= cutoffs.AMin ? TierGrade.A :
            strength >= cutoffs.BMin ? TierGrade.B :
            strength >= cutoffs.CMin ? TierGrade.C :
            TierGrade.D;

        // A thin sample cannot earn a top tier even if its shrunk delta clears the bar.
        if (isLowSample && (tier == TierGrade.S || tier == TierGrade.A))
            tier = TierGrade.B;

        return tier;
    }

    /// <summary>
    /// Method-of-moments Beta prior strength <c>k = m(1-m)/v - 1</c> fit over adequately-sampled champions'
    /// win rates. Every degenerate case (too few champions, zero/over-dispersed variance, non-finite result)
    /// clamps to <see cref="TieringOptions.PriorStrengthMax"/> — maximal shrinkage toward the baseline — so the
    /// scorer is always total.
    /// </summary>
    private static double FitPriorStrength(IReadOnlyList<ChampionAggregate> ordered, TieringOptions options)
    {
        var rates = new List<double>();
        foreach (var c in ordered)
            if (c.Games >= options.PriorFitMinGames && c.Games > 0)
                rates.Add((double)c.Wins / c.Games);

        if (rates.Count < 2)
            return options.PriorStrengthMax;

        double mean = 0;
        foreach (var p in rates) mean += p;
        mean /= rates.Count;

        double variance = 0;
        foreach (var p in rates) variance += (p - mean) * (p - mean);
        variance /= rates.Count;

        var spread = mean * (1 - mean);
        if (variance <= 0 || variance >= spread || spread <= 0
            || double.IsNaN(variance) || double.IsInfinity(variance))
            return options.PriorStrengthMax;

        var k = spread / variance - 1.0;
        if (double.IsNaN(k) || double.IsInfinity(k))
            return options.PriorStrengthMax;

        return Math.Clamp(k, options.PriorStrengthMin, options.PriorStrengthMax);
    }
}
