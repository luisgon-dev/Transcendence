namespace Transcendence.Data.Models.LoL.Analytics;

/// <summary>
/// Precomputed champion tier <i>grade</i> for one <c>(Patch, PlatformRegion, RankScope, Role, ChampionId)</c> —
/// the single source of truth every surface reads (tier list, champions grid, champion detail hero), so a
/// champion shows the same grade everywhere. Unlike the additive <see cref="ChampionRoleTierStat"/> atoms,
/// this is a fully-scored row: empirical-Bayes win-rate delta vs the role baseline, the absolute-cutoff tier,
/// the separate popularity/"contested" axis, and patch-over-patch movement.
/// <para>
/// Persisted only for <c>PlatformRegion = "ALL"</c> and <c>RankScope ∈ {"ALL", "EMERALD_PLUS"}</c> — the
/// scopes every web surface defaults to. Specific-region or exact-tier requests recompute on read via the
/// same scorer (no movement). One row per role plus a synthetic <c>Role = "ALL"</c> overview row that carries
/// the champion's primary (most-played) role grade (the graded role is in <see cref="PrimaryRole"/>).
/// </para>
/// <para>
/// <see cref="Tier"/>/<see cref="PreviousTier"/>/<see cref="Movement"/> are stored as their underlying ints
/// (the <c>TierGrade</c>/<c>TierMovement</c> enums live in <c>Transcendence.Service.Core</c>, which this
/// project must not reference); the read/scorer layer maps them back to the enums.
/// </para>
/// </summary>
public class ChampionScopeGradeStat
{
    public Guid Id { get; set; }

    /// <summary>Game version, e.g. "15.12" — matches <c>Match.Patch</c>.</summary>
    public string Patch { get; set; } = "";

    public string QueueFamily { get; set; } = "RANKED_SOLO_DUO";

    /// <summary>Persisted region scope — currently always the synthetic global <c>"ALL"</c>.</summary>
    public string PlatformRegion { get; set; } = "";

    /// <summary>Rank-scope token: <c>"ALL"</c> or <c>"EMERALD_PLUS"</c>.</summary>
    public string RankScope { get; set; } = "";

    /// <summary>Role partition: TOP/JUNGLE/MIDDLE/BOTTOM/UTILITY, or <c>"ALL"</c> for the overview row.</summary>
    public string Role { get; set; } = "";

    public int ChampionId { get; set; }

    /// <summary>The graded role — equal to <see cref="Role"/> for per-role rows; the champion's primary
    /// (most-played) role for the <c>"ALL"</c> overview row.</summary>
    public string PrimaryRole { get; set; } = "";

    /// <summary>Tier grade as the underlying <c>TierGrade</c> int (0=S … 4=D).</summary>
    public int Tier { get; set; }

    /// <summary>Signed win-rate delta vs the role baseline (empirical-Bayes posterior − baseline).</summary>
    public double StrengthScore { get; set; }

    /// <summary>Raw win rate (0..1) for display.</summary>
    public double WinRate { get; set; }

    public int Games { get; set; }
    public int Wins { get; set; }

    /// <summary>Pick rate within the role (games / Σ role games), 0..1.</summary>
    public double PickRate { get; set; }

    /// <summary>Ban rate over distinct matches in scope, 0..1.</summary>
    public double BanRate { get; set; }

    /// <summary>Popularity/meta-presence index (weighted champ-select presence + ban) — the "most contested" axis.</summary>
    public double ContestedScore { get; set; }

    /// <summary>The role baseline win rate (mu) this delta was measured against.</summary>
    public double RoleBaseline { get; set; }

    /// <summary>The empirical-Bayes prior strength k (pseudo-count) used for this role's shrinkage.</summary>
    public double PriorStrength { get; set; }

    /// <summary>True when below the games floor — capped at B, never S/A.</summary>
    public bool IsLowSample { get; set; }

    /// <summary><c>TierMovement</c> int vs the previous patch (0=NEW,1=UP,2=DOWN,3=SAME); null when not resolvable.</summary>
    public int? Movement { get; set; }

    /// <summary>Previous patch's <c>TierGrade</c> int; null if the champion was not graded last patch.</summary>
    public int? PreviousTier { get; set; }

    public DateTime ComputedAtUtc { get; set; }
}
