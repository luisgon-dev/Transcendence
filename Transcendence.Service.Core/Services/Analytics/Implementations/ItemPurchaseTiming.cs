using Transcendence.Data.Models.LoL.Analytics;
using Transcendence.Service.Core.Services.Analytics.Models;

namespace Transcendence.Service.Core.Services.Analytics.Implementations;

/// <summary>
/// Turns Build Lab's item counts into an item page's "when it's bought" stages. Pure arithmetic.
///
/// Each stage is one Build Lab decision pooled over every champion and role: the item against every
/// other item completed at the same stage. <see cref="BuildLabEstimator"/> does the rates, so the
/// gold-difference standardization and the lift shrinkage are the backtested ones Build Lab ships.
/// </summary>
public static class ItemPurchaseTiming
{
    private const string OtherItems = "other";

    /// <param name="item">This item's counts, any family.</param>
    /// <param name="population">Every item's counts in the same patch and region.</param>
    public static ItemPurchaseTimingDto? Estimate(
        int itemId,
        string patch,
        string region,
        IReadOnlyCollection<ItemPurchaseCount> item,
        IReadOnlyCollection<ItemPurchaseCount> population)
    {
        // An item is a legendary or boots, never both; whichever holds its games is its kind.
        var family = item
            .GroupBy(count => count.Family)
            .OrderByDescending(group => group.Sum(count => count.Games))
            .Select(group => (BuildLabFamily?)group.Key)
            .FirstOrDefault();
        if (family is null)
            return null;

        var actionKey = itemId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var stages = new List<ItemPurchaseStageDto>();
        foreach (var stage in item.Where(count => count.Family == family).GroupBy(count => count.Stage).OrderBy(g => g.Key))
        {
            var own = stage.ToDictionary(count => count.GoldBucket);
            var all = population
                .Where(count => count.Family == family && count.Stage == stage.Key)
                .ToDictionary(count => count.GoldBucket);
            var counts = new List<BuildLabCount>();
            foreach (var bucket in own.Keys.Union(all.Keys))
            {
                var mine = own.GetValueOrDefault(bucket);
                var total = all.GetValueOrDefault(bucket);
                if (mine is { Games: > 0 })
                    counts.Add(new BuildLabCount(actionKey, bucket, mine.Games, mine.Wins, mine.TimingSeconds));
                // The two reads are cached separately and the refresher adds between them, so the
                // population can briefly trail the item; clamp rather than count negative games.
                var otherGames = Math.Max(0, (total?.Games ?? 0) - (mine?.Games ?? 0));
                if (otherGames > 0)
                {
                    var otherWins = Math.Clamp((total?.Wins ?? 0) - (mine?.Wins ?? 0), 0, otherGames);
                    counts.Add(new BuildLabCount(OtherItems, bucket, otherGames, otherWins, 0));
                }
            }

            var estimate = BuildLabEstimator.Estimate(counts, parent: null, timed: true);
            var option = estimate.Options.FirstOrDefault(candidate => candidate.ActionKey == actionKey);
            if (option is null)
                continue;
            stages.Add(new ItemPurchaseStageDto(
                stage.Key,
                (int)option.Games,
                option.PickRate,
                option.WinRate,
                option.AdjustedWinRate,
                estimate.WinRate,
                option.ConfidenceLow,
                option.ConfidenceHigh,
                option.AverageTimingMinutes,
                option.IsLowSample));
        }
        if (stages.Count == 0)
            return null;

        var games = stages.Sum(stage => stage.Games);
        var timed = stages.Where(stage => stage.AverageMinutes is not null).ToList();
        double? averageMinutes = timed.Count > 0
            ? timed.Sum(stage => stage.AverageMinutes!.Value * stage.Games) / timed.Sum(stage => stage.Games)
            : null;
        return new ItemPurchaseTimingDto(
            family == BuildLabFamily.Boots ? "BOOTS" : "ITEM",
            patch,
            region,
            games,
            averageMinutes,
            stages);
    }
}
