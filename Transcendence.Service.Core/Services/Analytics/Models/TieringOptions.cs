namespace Transcendence.Service.Core.Services.Analytics.Models;

/// <summary>
/// Tuning knobs for the per-role-first, empirical-Bayes champion tier methodology
/// (see <c>ChampionTierScorer</c>). Bound from configuration section <c>Analytics:Tiering</c>
/// in both hosts. Defaults are sensible starting points — the absolute cutoffs and the prior
/// strengths are calibrated against live-patch scope volumes and remain config-overridable without
/// a logic redeploy.
/// </summary>
public class TieringOptions
{
    /// <summary>Absolute win-rate-delta thresholds (vs the role baseline) that cut the S/A/B/C/D tiers.</summary>
    public TierCutoffs Cutoffs { get; set; } = new();

    /// <summary>
    /// Lower clamp on the empirical-Bayes prior strength <c>k</c> (pseudo-count, in games). A floor of
    /// shrinkage so even a well-sampled role never trusts champion win rates with zero regularization.
    /// </summary>
    public double PriorStrengthMin { get; set; } = 50.0;

    /// <summary>
    /// Upper clamp on the prior strength <c>k</c>. Also the value used when the method-of-moments fit is
    /// degenerate (near-zero or over-dispersed variance) so the role collapses toward its baseline instead
    /// of producing a non-finite <c>k</c>.
    /// </summary>
    public double PriorStrengthMax { get; set; } = 2000.0;

    /// <summary>
    /// Lower bound for the adaptive games threshold used to include a champion in the Beta prior fit.
    /// </summary>
    public int PriorFitMinGamesFloor { get; set; } = 20;

    /// <summary>
    /// Upper bound for the adaptive games threshold used to include a champion in the Beta prior fit.
    /// </summary>
    public int PriorFitMinGamesCeiling { get; set; } = 200;

    /// <summary>Share of a role's games used to scale the prior-fit threshold between its bounds.</summary>
    public double PriorFitRoleVolumeShare { get; set; } = 0.0012;

    /// <summary>Lower bound for the adaptive S/A eligibility threshold.</summary>
    public int GradeMinGamesFloor { get; set; } = 50;

    /// <summary>Upper bound for the adaptive S/A eligibility threshold.</summary>
    public int GradeMinGamesCeiling { get; set; } = 500;

    /// <summary>
    /// Share of a role's games used to scale the S/A eligibility threshold between its bounds. A champion
    /// below the resolved threshold is flagged low-sample and capped at B.
    /// </summary>
    public double GradeRoleVolumeShare { get; set; } = 0.003;

    /// <summary>Weight on champion-select presence (games-per-match) in the "most contested" index.</summary>
    public double ContestPickWeight { get; set; } = 1.0;

    /// <summary>Weight on ban rate in the "most contested" index.</summary>
    public double ContestBanWeight { get; set; } = 1.0;
}

/// <summary>
/// Absolute cutoffs on the signed win-rate-delta (posterior win rate − role baseline). Honest tiers:
/// S can be empty on a balanced patch and overflow on a broken one. Monotonic: S ≥ <see cref="SMin"/>,
/// A ≥ <see cref="AMin"/>, B ≥ <see cref="BMin"/>, C ≥ <see cref="CMin"/>, else D.
/// </summary>
public class TierCutoffs
{
    public double SMin { get; set; } = 0.03;
    public double AMin { get; set; } = 0.015;
    public double BMin { get; set; } = -0.015;
    public double CMin { get; set; } = -0.03;
}
