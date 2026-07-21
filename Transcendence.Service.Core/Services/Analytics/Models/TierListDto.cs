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
/// Champion tier grade. Assigned by absolute cutoffs on the win-rate delta vs the role baseline
/// (see <c>ChampionTierScorer</c>) — S is a real, sample-resolvable edge, not a fixed top-N percentile,
/// so S can legitimately be empty on a balanced patch.
/// </summary>
public enum TierGrade
{
    S, A, B, C, D
}

/// <summary>
/// Single champion entry in tier list. For the unified ("All Roles") view each entry is the champion at its
/// primary role; <see cref="Role"/> carries that graded role. Movement is only populated for the persisted
/// region=ALL default scopes.
/// </summary>
public record TierListEntry(
    int ChampionId,
    string Role,
    TierGrade Tier,
    double StrengthScore,      // Signed win-rate delta vs the role baseline — the value tiers are cut on
    double WinRate,            // 0.0 to 1.0
    double PickRate,           // 0.0 to 1.0 (within-role pick share)
    double BanRate,            // 0.0 to 1.0
    int Games,                 // Sample size
    TierMovement? Movement,    // Compared to previous patch (persisted region=ALL grades only)
    TierGrade? PreviousTier,   // Null if NEW
    double ContestedScore = 0, // Popularity / meta-presence index (kept separate from strength)
    double RoleBaseline = 0,   // The role's baseline win rate this delta was measured against
    bool IsLowSample = false   // Below the games floor → capped at B, never S/A
);

/// <summary>
/// Complete tier list response grouped by tier grade.
/// </summary>
public record TierListResponse(
    string Patch,
    string? Role,             // Null if unified (all roles)
    string? RankTier,         // "all" for all ranks, or scope token (e.g., "EMERALD_PLUS")
    string Region,
    List<TierListEntry> Entries,
    AnalyticsSampleMetadata? Sample = null,
    // When the precomputed analytics for this patch were last refreshed (null while serving live compute).
    DateTime? ComputedAtUtc = null,
    string QueueFamily = "RANKED_SOLO_DUO"
);
