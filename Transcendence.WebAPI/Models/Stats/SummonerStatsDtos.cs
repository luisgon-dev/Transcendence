namespace Transcendence.WebAPI.Models.Stats;

public record SummonerOverviewDto(
    Guid SummonerId,
    int TotalMatches,
    int Wins,
    int Losses,
    double WinRate,
    double AvgKills,
    double AvgDeaths,
    double AvgAssists,
    double KdaRatio,
    double AvgCsPerMin,
    double AvgVisionScore,
    double AvgDamageToChamps,
    double AvgGameDurationMin,
    IReadOnlyList<RecentPerformanceDto> RecentPerformance
);
public record RecentPerformanceDto(
    string MatchId,
    bool Win,
    int Kills,
    int Deaths,
    int Assists,
    double CsPerMin,
    int VisionScore,
    int DamageToChamps
);
public record ChampionStatDto(
    int ChampionId,
    int Games,
    int Wins,
    int Losses,
    double WinRate,
    double AvgKills,
    double AvgDeaths,
    double AvgAssists,
    double KdaRatio,
    double AvgCsPerMin,
    double AvgVisionScore,
    double AvgDamageToChamps
);
public record RoleStatDto(
    string Role,
    int Games,
    int Wins,
    int Losses,
    double WinRate
);
public record RecentMatchSummaryDto(
    string MatchId,
    long MatchDate,
    int DurationSeconds,
    string QueueType,
    bool Win,
    int ChampionId,
    string? TeamPosition,
    int Kills,
    int Deaths,
    int Assists,
    int VisionScore,
    int DamageToChamps,
    double CsPerMin
);
public record PagedResultDto<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount, int TotalPages);
