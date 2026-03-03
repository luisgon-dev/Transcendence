namespace Transcendence.Service.Core.Services.Analysis.Models;

public record MultiSearchResult(
    IReadOnlyList<MultiSearchSummoner> Summoners,
    MultiSearchTeamAnalysis TeamAnalysis
);

public record MultiSearchSummoner(
    string GameName,
    string TagLine,
    bool Found,
    Guid? SummonerId = null,
    int? ProfileIconId = null,
    int? SummonerLevel = null,
    MultiSearchRank? SoloRank = null,
    MultiSearchRank? FlexRank = null,
    SummonerOverviewStats? Overview = null,
    IReadOnlyList<ChampionStat>? TopChampions = null,
    IReadOnlyList<RoleStat>? Roles = null,
    string? PrimaryRole = null
);

public record MultiSearchRank(
    string Tier,
    string Division,
    int LeaguePoints,
    int Wins,
    int Losses
);

public record MultiSearchTeamAnalysis(
    double AverageRankScore,
    string AverageRankLabel,
    IReadOnlyList<string> RoleCoverage,
    IReadOnlyList<string> MissingRoles,
    IReadOnlyList<MultiSearchAutofill> PotentialAutofills
);

public record MultiSearchAutofill(
    string GameName,
    string TagLine,
    string? PrimaryRole,
    string Note
);
