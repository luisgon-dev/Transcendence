namespace Transcendence.Service.Core.Services.Leaderboards.Models;

public sealed record LeaderboardResponse(
    string Region,
    string Queue,
    int? ChampionId,
    string? Role,
    DateTime GeneratedAtUtc,
    IReadOnlyList<LeaderboardEntry> Entries);

public sealed record LeaderboardEntry(
    int Position,
    Guid SummonerId,
    string GameName,
    string TagLine,
    int ProfileIconId,
    string? Tier,
    string? Division,
    int? LeaguePoints,
    int RankedWins,
    int RankedLosses,
    int? ChampionGames = null,
    int? ChampionWins = null,
    double? ChampionWinRate = null,
    double? ChampionKda = null,
    DateTime? UpdatedAtUtc = null);
