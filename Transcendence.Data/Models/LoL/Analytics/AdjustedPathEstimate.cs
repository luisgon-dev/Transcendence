namespace Transcendence.Data.Models.LoL.Analytics;

/// <summary>
/// Non-additive estimate for a complete selected item path.
/// </summary>
public class AdjustedPathEstimate
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
    public string PathHash { get; set; } = string.Empty;
    public string ItemPathJson { get; set; } = "[]";
    public double? EstimatedWinProbability { get; set; }
    public double? AdjustedLift { get; set; }
    public double? ConfidenceLow { get; set; }
    public double? ConfidenceHigh { get; set; }
    public long ObservedCount { get; set; }
    public double EffectiveSampleSize { get; set; }
    public bool IsPublishable { get; set; }
    public string? UnavailableReason { get; set; }
}
