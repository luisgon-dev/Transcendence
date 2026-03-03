using System.ComponentModel.DataAnnotations;

namespace Transcendence.WebAPI.Models.MultiSearch;

public class MultiSearchRequest
{
    /// <summary>
    /// Platform region (e.g., NA1, EUW1, KR). Common short forms (na, euw, kr) are also accepted.
    /// </summary>
    [Required]
    public string Region { get; set; } = string.Empty;

    /// <summary>
    /// List of summoners to look up. Maximum 5.
    /// </summary>
    [Required]
    [MinLength(1)]
    [MaxLength(5)]
    public List<MultiSearchSummonerInput> Summoners { get; set; } = [];
}

public class MultiSearchSummonerInput
{
    [Required]
    public string GameName { get; set; } = string.Empty;

    [Required]
    public string TagLine { get; set; } = string.Empty;
}

public record MultiSearchResponse(
    List<MultiSearchSummonerResult> Results,
    MultiSearchTeamInsights TeamInsights
);

public record MultiSearchSummonerResult(
    string GameName,
    string TagLine,
    bool Found,
    Guid? SummonerId = null,
    int? ProfileIconId = null,
    int? SummonerLevel = null,
    MultiSearchRankInfo? SoloRank = null,
    MultiSearchRankInfo? FlexRank = null,
    MultiSearchOverviewStats? OverviewStats = null,
    List<MultiSearchChampionStat>? TopChampions = null,
    List<MultiSearchRoleStat>? RoleDistribution = null,
    string? PrimaryRole = null
);

public record MultiSearchRankInfo(
    string Tier,
    string Division,
    int LeaguePoints,
    int Wins,
    int Losses
);

public record MultiSearchOverviewStats(
    int TotalMatches,
    int Wins,
    int Losses,
    double WinRate,
    double AvgKills,
    double AvgDeaths,
    double AvgAssists,
    double KdaRatio
);

public record MultiSearchChampionStat(
    int ChampionId,
    int Games,
    int Wins,
    double WinRate,
    double KdaRatio
);

public record MultiSearchRoleStat(
    string Role,
    int Games,
    double Percentage
);

public record MultiSearchTeamInsights(
    double AverageRankScore,
    string AverageRankLabel,
    List<string> RoleCoverage,
    List<string> MissingRoles,
    List<MultiSearchAutofillRisk> PotentialAutofills
);

public record MultiSearchAutofillRisk(
    string GameName,
    string TagLine,
    string? PrimaryRole,
    string Note
);
