namespace Transcendence.Data.Models.LoL.Analytics;

public enum BuildLabGenerationStatus
{
    PendingDataset = 0,
    Modeling = 1,
    Candidate = 2,
    Ready = 3,
    Failed = 4,
    Retired = 5
}

/// <summary>
/// Immutable provenance and validation manifest for one Build Lab serving generation.
/// Only a complete Ready generation may be made active.
/// </summary>
public class BuildLabGeneration
{
    public Guid Id { get; set; }
    public BuildLabGenerationStatus Status { get; set; } = BuildLabGenerationStatus.PendingDataset;
    public bool IsActive { get; set; }
    public string Patch { get; set; } = string.Empty;
    public string RankScope { get; set; } = "EMERALD_PLUS";
    public string DatasetVersion { get; set; } = "build-lab-v1";
    public string StaticDataVersion { get; set; } = string.Empty;
    public string ModelVersion { get; set; } = string.Empty;
    public string CodeRevision { get; set; } = string.Empty;
    public string IncludedPatchesJson { get; set; } = "[]";
    public string IncludedRegionsJson { get; set; } = "[]";
    public DateTime SourceCutoffUtc { get; set; }
    public long MatchCount { get; set; }
    public string? ArtifactUri { get; set; }
    public string? ArtifactSha256 { get; set; }
    public string ArtifactManifestJson { get; set; } = "{}";
    public string ValidationMetricsJson { get; set; } = "{}";
    public string? FailureReason { get; set; }

    /// <summary>
    /// Identity of the modeler process that claimed the generation, for diagnostics only.
    /// </summary>
    /// <remarks>
    /// Liveness is not tracked here. The modeler holds a PostgreSQL session advisory lock for the
    /// whole run, so a dead process releases it when its session drops and the coordinator decides
    /// abandonment by probing that lock. An expiry/heartbeat pair used to live alongside this and
    /// reaped six consecutive healthy runs, because the renewal thread could not win the GIL against
    /// a multi-minute load.
    /// </remarks>
    public string? LeaseOwner { get; set; }

    /// <summary>Append-only [{action, atUtc, actor, reason}] audit of promote/rollback/fail.</summary>
    public string PromotionHistoryJson { get; set; } = "[]";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAtUtc { get; set; }
    public DateTime? PromotedAtUtc { get; set; }
    public DateTime? RetiredAtUtc { get; set; }

    public ICollection<AdjustedActionEstimate> ActionEstimates { get; set; } =
        new List<AdjustedActionEstimate>();
    public ICollection<AdjustedPathEstimate> PathEstimates { get; set; } =
        new List<AdjustedPathEstimate>();
}
