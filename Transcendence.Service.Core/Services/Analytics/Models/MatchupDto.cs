namespace Transcendence.Service.Core.Services.Analytics.Models;

/// <summary>
/// Represents a single matchup entry with performance statistics.
/// </summary>
public record MatchupEntryDto
{
    /// <summary>
    /// The opponent champion ID.
    /// </summary>
    public int OpponentChampionId { get; init; }

    /// <summary>
    /// Total games played against this opponent.
    /// </summary>
    public int Games { get; init; }

    /// <summary>
    /// Wins against this opponent.
    /// </summary>
    public int Wins { get; init; }

    /// <summary>
    /// Losses against this opponent.
    /// </summary>
    public int Losses { get; init; }

    /// <summary>
    /// Win rate against this opponent (0.0 - 1.0).
    /// </summary>
    public double WinRate { get; init; }

    /// <summary>
    /// Average gold difference at 15 minutes from timeline frame-derived snapshots.
    /// </summary>
    public double? AvgGoldDiffAt15 { get; init; }

    /// <summary>
    /// Average XP difference at 15 minutes from timeline frame-derived snapshots.
    /// </summary>
    public double? AvgXpDiffAt15 { get; init; }
}

/// <summary>
/// Champion matchup data response showing counters and favorable matchups.
/// </summary>
public record ChampionMatchupsResponse
{
    /// <summary>
    /// The champion ID for which matchups are calculated.
    /// </summary>
    public int ChampionId { get; init; }

    /// <summary>
    /// The role/lane (e.g., "MID", "TOP").
    /// </summary>
    public string Role { get; init; } = string.Empty;

    /// <summary>
    /// The rank tier filter (e.g., "PLATINUM").
    /// </summary>
    public string? RankTier { get; init; }

    /// <summary>
    /// The patch version for which matchups are calculated.
    /// </summary>
    public string Patch { get; init; } = string.Empty;

    /// <summary>
    /// Top 5 counters (champions that beat you, win rate &lt; 48%).
    /// </summary>
    public List<MatchupEntryDto> Counters { get; init; } = new();

    /// <summary>
    /// Top 5 favorable matchups (champions you beat, win rate &gt; 52%).
    /// </summary>
    public List<MatchupEntryDto> FavorableMatchups { get; init; } = new();

    /// <summary>
    /// Full matchup universe sorted by games descending by default.
    /// </summary>
    public List<MatchupEntryDto> AllMatchups { get; init; } = new();

    /// <summary>
    /// Ratio of matchup games where both lane opponents had timeline @15 snapshots.
    /// </summary>
    public double? TimelineCoverageRatio { get; init; }

    /// <summary>
    /// Count of matchup games contributing to timeline-derived @15 deltas.
    /// </summary>
    public int TimelineSampleSize { get; init; }

    /// <summary>
    /// Latest timestamp when contributing timeline rows were derived.
    /// </summary>
    public DateTime? TimelineDataFreshnessUtc { get; init; }
}
