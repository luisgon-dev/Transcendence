namespace Transcendence.Service.Core.Services.Analysis.Models;

public record MatchHistoryQueueFacet(int QueueId, string QueueType, string QueueFamily);

public record MatchHistoryFacets(
    IReadOnlyList<MatchHistoryQueueFacet> Queues,
    IReadOnlyList<int> ChampionIds);

public record PagedResult<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    int TotalCount,
    MatchHistoryFacets? Facets = null)
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

public record SummonerSeasonProfileStats(
    string SeasonKey,
    string SeasonDisplayName,
    string QueueScope,
    SummonerOverviewStats Overview,
    IReadOnlyList<ChampionStat> Champions,
    SummonerFullHistoryProfileStatus? FullHistory);

public record SummonerFullHistoryProfileStatus(
    string Status,
    DateTime RequestedAtUtc,
    DateTime? StartedAtUtc,
    DateTime? CompletedAtUtc,
    DateTime UpdatedAtUtc,
    int PagesScanned,
    int MatchIdsDiscovered,
    int FactsPersisted,
    int DetailFetchFailures,
    int CompletedMatchCount,
    int? RiotWins,
    int? RiotLosses,
    int? RiotTotal,
    int? RankedCountDelta,
    string? CoverageStatus,
    int ClassifierVersion);

public record RoleStat(
    string Role,
    int Games,
    int Wins,
    int Losses,
    double WinRate
);

public record RankHistoryEntry(
    string? QueueType,
    string? Tier,
    string? RankNumber,
    int LeaguePoints,
    int Wins,
    int Losses,
    DateTime DateRecorded
);

public record PlayedWithEntry(
    Guid SummonerId,
    string? GameName,
    string? TagLine,
    int GamesTogether,
    int SameTeamGames,
    int SameTeamWins
);

public record ChampionMasteryEntry(
    int ChampionId,
    int ChampionLevel,
    long ChampionPoints,
    long LastPlayTime,
    bool ChestGranted,
    int TokensEarned
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
