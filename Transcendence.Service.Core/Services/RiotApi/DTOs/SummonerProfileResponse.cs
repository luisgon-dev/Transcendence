namespace Transcendence.Service.Core.Services.RiotApi.DTOs;

public class SummonerProfileResponse
{
    public Guid SummonerId { get; set; }
    public string Puuid { get; set; } = string.Empty;
    public string GameName { get; set; } = string.Empty;
    public string TagLine { get; set; } = string.Empty;
    public int SummonerLevel { get; set; }
    public int ProfileIconId { get; set; }

    // Rank data
    public RankInfo? SoloRank { get; set; }
    public RankInfo? FlexRank { get; set; }

    // Overview statistics (from all matches)
    public ProfileOverviewStats? OverviewStats { get; set; }

    // Top champions by games played
    public List<ProfileChampionStat>? TopChampions { get; set; }

    // Recent match summaries (last 10)
    public List<ProfileRecentMatch>? RecentMatches { get; set; }

    // Data freshness
    public DataAgeMetadata ProfileAge { get; set; } = new();
    public DataAgeMetadata RankAge { get; set; } = new();

    /// <summary>
    /// Data freshness for stats (based on most recent match).
    /// Null if no match data available.
    /// </summary>
    public DataAgeMetadata? StatsAge { get; set; }
}

public class RankInfo
{
    public string Tier { get; set; } = string.Empty;
    public string Division { get; set; } = string.Empty;
    public int LeaguePoints { get; set; }
    public int Wins { get; set; }
    public int Losses { get; set; }
}

/// <summary>
/// Overview statistics for the profile response.
/// </summary>
public class ProfileOverviewStats
{
    public int TotalMatches { get; set; }
    public int Wins { get; set; }
    public int Losses { get; set; }
    public double WinRate { get; set; }
    public double AvgKills { get; set; }
    public double AvgDeaths { get; set; }
    public double AvgAssists { get; set; }
    public double KdaRatio { get; set; }
    public double AvgCsPerMin { get; set; }
    public double AvgVisionScore { get; set; }
    public double AvgDamageToChamps { get; set; }
}

/// <summary>
/// Champion statistics for the profile response.
/// </summary>
public class ProfileChampionStat
{
    public int ChampionId { get; set; }
    public string ChampionName { get; set; } = string.Empty;
    public int Games { get; set; }
    public int Wins { get; set; }
    public int Losses { get; set; }
    public double WinRate { get; set; }
    public double KdaRatio { get; set; }
}

/// <summary>
/// Recent match summary for the profile response.
/// </summary>
public class ProfileRecentMatch
{
    public string MatchId { get; set; } = string.Empty;
    public long MatchDate { get; set; }
    public string QueueType { get; set; } = string.Empty;
    public bool Win { get; set; }
    public int ChampionId { get; set; }
    public string ChampionName { get; set; } = string.Empty;
    public int Kills { get; set; }
    public int Deaths { get; set; }
    public int Assists { get; set; }
    public double CsPerMin { get; set; }
}
