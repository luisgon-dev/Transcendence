namespace Transcendence.Service.Core.Services.Tft.Models;

/// <summary>
/// Result of a TFT multi-search operation.
/// </summary>
public record TftMultiSearchResult(
    IReadOnlyList<TftMultiSearchSummoner> Summoners,
    TftMultiSearchLobbyAnalysis LobbyAnalysis
);

/// <summary>
/// Individual summoner result in a TFT multi-search.
/// </summary>
public record TftMultiSearchSummoner(
    string GameName,
    string TagLine,
    bool Found,
    Guid? SummonerId = null,
    int? ProfileIconId = null,
    long? SummonerLevel = null,
    TftMultiSearchRank? Rank = null,
    TftSummonerOverviewStats? Overview = null,
    IReadOnlyList<TftChampionStat>? TopUnits = null
);

/// <summary>
/// Rank information for TFT multi-search.
/// </summary>
public record TftMultiSearchRank(
    string Tier,
    string Division,
    int LeaguePoints,
    int Wins,
    int Losses
);

/// <summary>
/// Analysis of the TFT lobby based on multi-search results.
/// </summary>
public record TftMultiSearchLobbyAnalysis(
    double AverageRankScore,
    string AverageRankLabel,
    int FoundPlayers,
    int TotalPlayers,
    IReadOnlyList<TftMultiSearchAutofill> PotentialIssues
);

/// <summary>
/// Potential issue detected in multi-search.
/// </summary>
public record TftMultiSearchAutofill(
    string GameName,
    string TagLine,
    string Note
);
