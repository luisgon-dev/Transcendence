using Transcendence.Service.Core.Services.Analytics.Models;

namespace Transcendence.Service.Core.Services.Analytics.Implementations;

/// <summary>One (option, gold bucket) count, already weighted by patch recency.</summary>
public readonly record struct BuildLabCount(
    string ActionKey,
    short GoldBucket,
    double Games,
    double Wins,
    double TimingSeconds);

public sealed record BuildLabCellEstimate(
    double Games,
    double WinRate,
    IReadOnlyList<BuildLabOptionDto> Options);

/// <summary>
/// Turns one decision's counts into per-option rates. Pure arithmetic, no data access.
///
/// The adjusted win rate is observed-minus-expected standardization over team gold difference. Each
/// game an option was chosen in is compared with the decision's win rate in that game's gold bucket,
/// and the option's lift is the average of those differences; its adjusted win rate is the decision's
/// overall win rate plus that lift. An item bought only while far ahead is therefore judged against
/// other far-ahead games, not credited for the lead -- which a per-option bucket rate cannot do, since
/// an option absent from a bucket has no rate there to reweight.
/// </summary>
public static class BuildLabEstimator
{
    /// <summary>Below this many games an option's interval is too wide to rank on.</summary>
    public const double LowSampleGames = 100;

    /// <summary>Pseudo-games pulling a matchup or regional estimate toward the all-games estimate.</summary>
    public const double ScopeShrinkageGames = 100;

    private const double Z95 = 1.959964;

    /// <param name="counts">Every count for one decision cell.</param>
    /// <param name="parent">
    /// The same decision estimated over all games, when <paramref name="counts"/> is a narrower
    /// scope. Each option's lift is shrunk toward its lift over all games, or toward no lift when the
    /// option never appears there.
    /// </param>
    /// <param name="timed">Whether the decision happens in game and so has a timing worth reporting.</param>
    public static BuildLabCellEstimate Estimate(
        IReadOnlyCollection<BuildLabCount> counts,
        BuildLabCellEstimate? parent,
        bool timed)
    {
        var cellGames = counts.Sum(count => count.Games);
        if (cellGames <= 0)
            return new BuildLabCellEstimate(0, 0, []);
        var cellWinRate = counts.Sum(count => count.Wins) / cellGames;
        var bucketWinRates = counts
            .GroupBy(count => count.GoldBucket)
            .ToDictionary(
                group => group.Key,
                group => group.Sum(count => count.Wins) / Math.Max(group.Sum(count => count.Games), double.Epsilon));
        var parentOptions = parent?.Options.ToDictionary(option => option.ActionKey);

        var options = new List<BuildLabOptionDto>();
        foreach (var option in counts.GroupBy(count => count.ActionKey))
        {
            var games = option.Sum(count => count.Games);
            if (games <= 0)
                continue;
            var wins = option.Sum(count => count.Wins);
            var winRate = wins / games;
            var expectedWins = option.Sum(count => count.Games * bucketWinRates[count.GoldBucket]);
            var lift = (wins - expectedWins) / games;
            // Agresti-Coull style centre so an option with every game won or lost still gets an interval.
            var centred = (wins + 1) / (games + 2);
            var variance = centred * (1 - centred) / games;

            if (parent != null)
            {
                var targetLift = parentOptions!.TryGetValue(option.Key, out var parentOption)
                    ? parentOption.Lift
                    : 0;
                var keep = games / (games + ScopeShrinkageGames);
                lift = keep * lift + (1 - keep) * targetLift;
                variance *= keep * keep;
            }

            var adjusted = Math.Clamp(cellWinRate + lift, 0, 1);
            var margin = Z95 * Math.Sqrt(variance);
            var timing = timed ? option.Sum(count => count.TimingSeconds) / games / 60.0 : (double?)null;
            options.Add(new BuildLabOptionDto(
                option.Key,
                BuildLabPath.ParseActionKey(option.Key),
                games,
                games / cellGames,
                winRate,
                adjusted,
                adjusted - cellWinRate,
                Math.Max(0, adjusted - margin),
                Math.Min(1, adjusted + margin),
                timing is > 0 ? timing : null,
                games < LowSampleGames));
        }

        return new BuildLabCellEstimate(cellGames, cellWinRate, options);
    }

    /// <summary>A choice needs this share of a decision's games to be recommended on its own.</summary>
    public const double RecommendationMinimumPickRate = 0.05;

    /// <summary>
    /// The one choice to recommend: the highest adjusted win rate among choices common enough to be a
    /// real option (at least <see cref="RecommendationMinimumPickRate"/> of the decision's games) and
    /// with enough games to trust.
    ///
    /// Not the SUPPORTED ranking's top row. That orders by the interval's lower bound, which the most
    /// played choice nearly always wins on sample size alone -- on prod it recommended a rune page
    /// running 0.6pp below average over two alternatives running 2pp above it.
    /// </summary>
    public static BuildLabOptionDto? Recommend(IEnumerable<BuildLabOptionDto> options)
    {
        var trusted = options.Where(option => !option.IsLowSample).ToList();
        return trusted
                   .Where(option => option.PickRate >= RecommendationMinimumPickRate)
                   .OrderByDescending(option => option.AdjustedWinRate)
                   .ThenByDescending(option => option.Games)
                   .FirstOrDefault()
               ?? trusted.OrderByDescending(option => option.ConfidenceLow).FirstOrDefault();
    }

    /// <summary>
    /// SUPPORTED ranks by the interval's lower bound (the option most likely to be genuinely good),
    /// IMPACT by lift, COMMON by pick rate. Low-sample options always sort after the rest.
    /// </summary>
    public static IEnumerable<BuildLabOptionDto> Rank(IEnumerable<BuildLabOptionDto> options, string mode)
    {
        var ordered = options.OrderBy(option => option.IsLowSample);
        return mode switch
        {
            "IMPACT" => ordered.ThenByDescending(option => option.Lift).ThenByDescending(option => option.Games),
            "COMMON" => ordered.ThenByDescending(option => option.PickRate),
            _ => ordered.ThenByDescending(option => option.ConfidenceLow).ThenByDescending(option => option.Games)
        };
    }
}
