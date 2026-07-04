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

    public ProfileSeasonMetadata? ActiveSeason { get; set; }
    public ProfileFullHistoryStatus? FullHistory { get; set; }

    // Summoners this player most frequently appears in matches with
    public List<FrequentlyPlayedWithStat>? FrequentlyPlayedWith { get; set; }

    // Highest champion mastery (by points)
    public List<ChampionMasteryStat>? TopMastery { get; set; }

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

public class ProfileSeasonMetadata
{
    public string SeasonKey { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string QueueScope { get; set; } = string.Empty;
}

public class ProfileFullHistoryStatus
{
    public string Status { get; set; } = string.Empty;
    public DateTime RequestedAtUtc { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public int PagesScanned { get; set; }
    public int MatchIdsDiscovered { get; set; }
    public int FactsPersisted { get; set; }
    public int DetailFetchFailures { get; set; }
    public int CompletedMatchCount { get; set; }
    public int? RiotWins { get; set; }
    public int? RiotLosses { get; set; }
    public int? RiotTotal { get; set; }
    public int? RankedCountDelta { get; set; }
    public string? CoverageStatus { get; set; }
    public int ClassifierVersion { get; set; }
}

/// <summary>
/// A summoner frequently appearing in the profile owner's matches.
/// </summary>
public class FrequentlyPlayedWithStat
{
    public Guid SummonerId { get; set; }
    public string GameName { get; set; } = string.Empty;
    public string TagLine { get; set; } = string.Empty;
    public int GamesTogether { get; set; }
    public int SameTeamGames { get; set; }
    public int SameTeamWins { get; set; }
}

/// <summary>
/// A champion-mastery entry for the profile response.
/// </summary>
public class ChampionMasteryStat
{
    public int ChampionId { get; set; }
    public string ChampionName { get; set; } = string.Empty;
    public int ChampionLevel { get; set; }
    public long ChampionPoints { get; set; }
    public long LastPlayTime { get; set; }
    public bool ChestGranted { get; set; }
    public int TokensEarned { get; set; }
}
