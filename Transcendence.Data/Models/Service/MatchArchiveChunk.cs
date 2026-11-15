namespace Transcendence.Data.Models.Service;

/// <summary>Durable ownership of a frozen raw-match cohort. Compact facts have no FK to this work.</summary>
public sealed class MatchArchiveChunk
{
    public Guid Id { get; set; }
    public string Patch { get; set; } = "";
    public string RelativePath { get; set; } = "";
    public int FrozenMatches { get; set; }
    public int PrunedMatches { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? VerifiedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public bool LegacySource { get; set; }
    public string ManifestJson { get; set; } = "{}";
    public string? LastError { get; set; }
}

public sealed class MatchArchiveEntry
{
    public Guid ChunkId { get; set; }
    // Deliberately no FK to Matches: deleting the source and this checkpoint is one transaction.
    public Guid MatchId { get; set; }
    public MatchArchiveChunk Chunk { get; set; } = null!;
}
