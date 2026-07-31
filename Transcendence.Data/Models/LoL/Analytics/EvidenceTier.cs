namespace Transcendence.Data.Models.LoL.Analytics;

/// <summary>
/// How precisely an estimate may be described publicly.
/// </summary>
/// <remarks>
/// Publication is deliberately not all-or-nothing. Patches ship fortnightly, and a cell needs far
/// more evidence to support a &lt;=3pp interval than to support a direction, so gating everything on
/// the interval leaves the lab empty for most of a patch. Ranking always uses the posterior mean;
/// this only decides how much of it is shown.
/// </remarks>
public enum EvidenceTier
{
    /// <summary>Pick rate and timing only — no claim about the effect.</summary>
    Descriptive = 0,

    /// <summary>Direction only: the posterior concentrates in one bucket, but no number is shown.</summary>
    Bucketed = 1,

    /// <summary>Full adjusted WPA with its interval; every v1 gate passed.</summary>
    Numeric = 2
}
