namespace Transcendence.Data.Models.LoL.Analytics;

/// <summary>
/// One model-produced action estimate. All probabilities and WPA values are stored as fractions
/// (0.01 means one percentage point) to match the existing analytics contracts.
/// </summary>
public class AdjustedActionEstimate
{
    public Guid Id { get; set; }
    public Guid GenerationId { get; set; }
    public required BuildLabGeneration Generation { get; set; }

    public int ChampionId { get; set; }
    public string Role { get; set; } = string.Empty;
    /// <summary>Lane opponent champion id, or 0 for the champion-role baseline.</summary>
    public int OpponentChampionId { get; set; }
    public string Patch { get; set; } = string.Empty;
    public string RegionScope { get; set; } = "GLOBAL";
    public string DecisionFamily { get; set; } = string.Empty;
    public int Stage { get; set; }
    public string PathPrefixHash { get; set; } = string.Empty;
    public string PathPrefixJson { get; set; } = "[]";
    public string ActionKey { get; set; } = string.Empty;
    public string ActionIdsJson { get; set; } = "[]";

    public double? AdjustedWpa { get; set; }
    public double? ConfidenceLow { get; set; }
    public double? ConfidenceHigh { get; set; }
    public double RawWinRate { get; set; }
    public double PickRate { get; set; }
    public long ObservedCount { get; set; }
    public double EffectiveSampleSize { get; set; }
    public double? AverageTimingMinutes { get; set; }
    public double PropensityOverlap { get; set; }
    public double CovariateBalance { get; set; }
    public bool StableAcrossFolds { get; set; }
    public bool IsPublishable { get; set; }
    public string EvidenceQuality { get; set; } = "INSUFFICIENT";
    public string FallbackScope { get; set; } = "NONE";
    /// <summary>Names the comparison set this estimate was measured against, per decision family.</summary>
    public string BaselineDefinition { get; set; } = string.Empty;
    public string? UnavailableReason { get; set; }
    public DateTime ComputedAtUtc { get; set; }
}
