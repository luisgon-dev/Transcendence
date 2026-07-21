namespace Transcendence.Data.Repositories.Interfaces;

public interface ILeaderboardRepository
{
    Task<IReadOnlyList<RegionalLeaderboardRow>> GetRegionalAsync(
        string platformRegion,
        bool rankedFlex,
        int limit,
        CancellationToken ct = default);

    Task<IReadOnlyList<ChampionLeaderboardRow>> GetChampionAsync(
        string platformRegion,
        int queueId,
        int championId,
        string? role,
        int minimumGames,
        int limit,
        CancellationToken ct = default);
}

public sealed record RegionalLeaderboardRow(
    Guid SummonerId,
    string GameName,
    string TagLine,
    int ProfileIconId,
    string Tier,
    string Division,
    int LeaguePoints,
    int Wins,
    int Losses,
    DateTime RankUpdatedAtUtc);

public sealed record ChampionLeaderboardRow(
    Guid SummonerId,
    string GameName,
    string TagLine,
    int ProfileIconId,
    string? Tier,
    string? Division,
    int? LeaguePoints,
    int RankedWins,
    int RankedLosses,
    int ChampionGames,
    int ChampionWins,
    long TotalKills,
    long TotalDeaths,
    long TotalAssists,
    DateTime UpdatedAtUtc);
