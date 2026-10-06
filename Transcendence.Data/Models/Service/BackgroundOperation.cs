namespace Transcendence.Data.Models.Service;

/// <summary>A shared durable execution; callers receive separately owned request IDs.</summary>
public sealed class BackgroundOperation
{
    public Guid Id { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string ResourceKey { get; set; } = string.Empty;
    public string PlatformRegion { get; set; } = string.Empty;
    public string GameName { get; set; } = string.Empty;
    public string TagLine { get; set; } = string.Empty;
    public Guid? LeaseToken { get; set; }
    public Guid? ParentId { get; set; }
    public BackgroundOperation? Parent { get; set; }
    public string Status { get; set; } = "queued";
    public DateTime QueuedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAtUtc { get; set; }
    public DateTime? RetryAtUtc { get; set; }
    public string? ErrorCode { get; set; }
    public string PhasesJson { get; set; } = "{}";
    public string ResultJson { get; set; } = "{}";
    public int Revision { get; set; }
    public string DispatchJson { get; set; } = "{}";
    public DateTime? DispatchedAtUtc { get; set; }
    public string? HangfireJobId { get; set; }
}

/// <summary>Opaque request ID granting one authenticated identity access to an execution.</summary>
public sealed class BackgroundOperationRequest
{
    public Guid Id { get; set; }
    public Guid ExecutionId { get; set; }
    public BackgroundOperation Execution { get; set; } = null!;
    public string OwnerKind { get; set; } = string.Empty;
    public Guid OwnerId { get; set; }
    public DateTime RequestedAtUtc { get; set; } = DateTime.UtcNow;
}
