namespace Transcendence.Service.Core.Services.Analysis.Models;

public record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
}

public record SummonerOverviewStats(
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
    IReadOnlyList<RecentPerformancePoint> RecentPerformance // e.g., last N games WR trend
);

public record RecentPerformancePoint(
    string MatchId,
    bool Win,
    int Kills,
    int Deaths,
    int Assists,
    double CsPerMin,
    int VisionScore,
    int DamageToChamps);

public record ChampionStat(
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

public record RoleStat(
    string Role,
    int Games,
    int Wins,
    int Losses,
    double WinRate
);

public record RecentMatchSummary(
    string MatchId,
    long MatchDate,
    int DurationSeconds,
    int QueueId,
    string QueueType,
    bool Win,
    int ChampionId,
    string? TeamPosition,
    int Kills,
    int Deaths,
    int Assists,
    int VisionScore,
    int DamageToChamps,
    double CsPerMin,
    int SummonerSpell1Id,
    int SummonerSpell2Id,
    IReadOnlyList<int> Items,  // 7 item IDs (6 items + trinket), 0 for empty slots
    MatchRuneSummary Runes,
    MatchRuneDetail RunesDetail
);

public record MatchRuneSummary(
    int PrimaryStyleId,
    int SubStyleId,
    int KeystoneId  // First rune in primary tree (the keystone)
);

public record MatchRuneDetail(
    int PrimaryStyleId,
    int SubStyleId,
    IReadOnlyList<int> PrimarySelections,
    IReadOnlyList<int> SubSelections,
    IReadOnlyList<int> StatShards
);
