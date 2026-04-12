namespace Transcendence.Service.Core.Services.Tft.Models;

/// <summary>
/// Overview statistics for a TFT summoner.
/// </summary>
public record TftSummonerOverviewStats(
    Guid SummonerId,
    int TotalMatches,
    double AveragePlacement,
    double Top4Rate,
    double WinRate,
    IReadOnlyList<TftRecentPerformanceStats> RecentPerformance
);

/// <summary>
/// Recent match performance summary for TFT.
/// </summary>
public record TftRecentPerformanceStats(
    string MatchId,
    long MatchDate,
    int Placement,
    int Level,
    string? SetCoreName,
    string? Patch,
    IReadOnlyList<TftUnitPerformanceSummary> Units,
    IReadOnlyList<TftTraitPerformanceSummary> Traits
);

/// <summary>
/// Unit summary for performance tracking.
/// </summary>
public record TftUnitPerformanceSummary(
    string CharacterId,
    string? Name,
    int Tier,
    IReadOnlyList<int> Items
);

/// <summary>
/// Trait summary for performance tracking.
/// </summary>
public record TftTraitPerformanceSummary(
    string Name,
    int NumUnits,
    int TierCurrent
);

/// <summary>
/// Statistics for a specific champion/unit in TFT.
/// </summary>
public record TftChampionStat(
    string CharacterId,
    string? Name,
    int Cost,
    int GamesPlayed,
    double AveragePlacement,
    double Top4Rate,
    double WinRate
);
