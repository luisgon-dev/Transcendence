namespace Transcendence.Service.Core.Services.LiveGame.Models;

public record LiveGameChampionPoolEntryDto(
    int ChampionId,
    int Games,
    double WinRate
);

public record LiveGameParticipantAnalysisDto(
    string Puuid,
    int TeamId,
    int ChampionId,
    string? RankTier,
    string? RankDivision,
    int? LeaguePoints,
    double? RecentWinRate,
    double? RecentKda,
    double? ChampionWinRate,
    int RecentGames,
    int CurrentStreak,
    List<LiveGameChampionPoolEntryDto> ChampionPool
);

public record TeamAnalysisDto(
    int TeamId,
    double AverageRecentWinRate,
    double AverageChampionWinRate,
    double AverageRankScore,
    double CompositeScore,
    double EstimatedWinProbability,
    List<string> Strengths,
    List<string> Weaknesses
);

public record LiveGameAnalysisDto(
    DateTime GeneratedAtUtc,
    List<LiveGameParticipantAnalysisDto> Participants,
    List<TeamAnalysisDto> Teams
);
