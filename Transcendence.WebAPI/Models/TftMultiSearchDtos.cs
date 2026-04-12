namespace Transcendence.WebAPI.Models;

/// <summary>
/// Request body for TFT multi-search.
/// </summary>
public record TftMultiSearchRequest(
    string Region,
    IReadOnlyList<string> RiotIds
);

/// <summary>
/// Response for TFT multi-search.
/// </summary>
public record TftMultiSearchResponse(
    IReadOnlyList<TftMultiSearchSummonerDto> Summoners,
    TftMultiSearchLobbyAnalysisDto LobbyAnalysis
);

/// <summary>
/// Individual summoner result in TFT multi-search.
/// </summary>
public record TftMultiSearchSummonerDto(
    string GameName,
    string TagLine,
    bool Found,
    Guid? SummonerId,
    int? ProfileIconId,
    long? SummonerLevel,
    TftMultiSearchRankDto? Rank,
    TftMultiSearchOverviewDto? Overview,
    IReadOnlyList<TftMultiSearchChampionDto>? TopUnits
);

/// <summary>
/// Rank information for TFT multi-search.
/// </summary>
public record TftMultiSearchRankDto(
    string Tier,
    string Division,
    int LeaguePoints,
    int Wins,
    int Losses
);

/// <summary>
/// Overview stats for TFT multi-search.
/// </summary>
public record TftMultiSearchOverviewDto(
    int TotalMatches,
    double AveragePlacement,
    double Top4Rate,
    double WinRate
);

/// <summary>
/// Champion/Unit info for TFT multi-search.
/// </summary>
public record TftMultiSearchChampionDto(
    string CharacterId,
    string? Name,
    int Cost,
    int GamesPlayed,
    double AveragePlacement,
    double Top4Rate,
    double WinRate
);

/// <summary>
/// Lobby analysis for TFT multi-search.
/// </summary>
public record TftMultiSearchLobbyAnalysisDto(
    double AverageRankScore,
    string AverageRankLabel,
    int FoundPlayers,
    int TotalPlayers,
    IReadOnlyList<TftMultiSearchAutofillDto> PotentialIssues
);

/// <summary>
/// Potential issue in TFT multi-search.
/// </summary>
public record TftMultiSearchAutofillDto(
    string GameName,
    string TagLine,
    string Note
);
