namespace Transcendence.WebAPI.Models.Stats;

/// <summary>
/// Overview statistics for a TFT summoner.
/// </summary>
public record TftSummonerOverviewDto(
    Guid SummonerId,
    int TotalMatches,
    double AveragePlacement,
    double Top4Rate,
    double WinRate,
    IReadOnlyList<TftRecentPerformanceDto> RecentPerformance
);

/// <summary>
/// Recent TFT match performance summary.
/// </summary>
public record TftRecentPerformanceDto(
    string MatchId,
    long MatchDate,
    int Placement,
    int Level,
    string? SetCoreName,
    string? Patch,
    IReadOnlyList<TftUnitPerformanceDto> Units,
    IReadOnlyList<TftTraitPerformanceDto> Traits
);

/// <summary>
/// Unit summary for TFT performance tracking.
/// </summary>
public record TftUnitPerformanceDto(
    string CharacterId,
    string? Name,
    int Tier,
    IReadOnlyList<int> Items
);

/// <summary>
/// Trait summary for TFT performance tracking.
/// </summary>
public record TftTraitPerformanceDto(
    string Name,
    int NumUnits,
    int TierCurrent
);

/// <summary>
/// Statistics for a specific champion/unit in TFT.
/// </summary>
public record TftChampionStatDto(
    string CharacterId,
    string? Name,
    int Cost,
    int GamesPlayed,
    double AveragePlacement,
    double Top4Rate,
    double WinRate
);
