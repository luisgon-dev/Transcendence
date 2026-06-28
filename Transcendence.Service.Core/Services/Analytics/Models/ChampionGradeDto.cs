namespace Transcendence.Service.Core.Services.Analytics.Models;

/// <summary>
/// A champion's tier grade for a specific <c>(role, rank scope)</c> — the SAME grade the tier list shows for
/// that champion in that role. Surfaced on the champion profile so the detail-page hero renders one consistent
/// grade instead of recomputing its own. <see cref="Movement"/>/<see cref="PreviousTier"/> are populated only
/// for the persisted region=ALL default scopes.
/// </summary>
public record ChampionGradeDto(
    TierGrade Tier,
    double StrengthScore,      // Signed win-rate delta vs the role baseline
    double WinRate,            // 0.0 to 1.0
    double PickRate,           // 0.0 to 1.0 (within-role)
    double BanRate,            // 0.0 to 1.0
    double ContestedScore,     // Popularity / meta-presence index
    int Games,
    double RoleBaseline,       // The role's baseline win rate this delta was measured against
    bool IsLowSample,          // Below the games floor → capped at B
    TierMovement? Movement,    // vs previous patch (region=ALL grades only)
    TierGrade? PreviousTier,
    string Role,               // The graded role
    string RankScope           // The rank scope the grade was computed for ("all", "EMERALD_PLUS", …)
);
