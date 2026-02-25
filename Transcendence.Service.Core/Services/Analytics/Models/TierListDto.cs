namespace Transcendence.Service.Core.Services.Analytics.Models;

/// <summary>
/// Movement indicator for tier list entry compared to previous patch.
/// </summary>
public enum TierMovement
{
    /// <summary>Champion is new to tier list (did not meet threshold in previous patch)</summary>
    NEW,

    /// <summary>Champion moved up from previous patch (lower tier letter or better percentile)</summary>
    UP,

    /// <summary>Champion moved down from previous patch (higher tier letter or worse percentile)</summary>
    DOWN,

    /// <summary>Champion stayed in same tier as previous patch</summary>
    SAME
}

/// <summary>
/// Tier grade for champion based on percentile ranking.
/// S = Top 10%, A = 10-30%, B = 30-60%, C = 60-85%, D = 85%+
/// </summary>
public enum TierGrade
{
    S, A, B, C, D
}

/// <summary>
/// Single champion entry in tier list with ranking and movement data.
/// </summary>
public record TierListEntry(
    int ChampionId,
    string Role,
    TierGrade Tier,
    double CompositeScore,    // 0.0 to 1.0 (70% win rate + 30% pick rate)
    double WinRate,           // 0.0 to 1.0
    double PickRate,          // 0.0 to 1.0
    double BanRate,           // 0.0 to 1.0
    int Games,                // Sample size
    TierMovement Movement,    // Compared to previous patch
    TierGrade? PreviousTier   // Null if NEW
);

/// <summary>
/// Complete tier list response grouped by tier grade.
/// </summary>
public record TierListResponse(
    string Patch,
    string? Role,             // Null if unified (all roles)
    string? RankTier,         // Null if all ranks
    List<TierListEntry> Entries
);
