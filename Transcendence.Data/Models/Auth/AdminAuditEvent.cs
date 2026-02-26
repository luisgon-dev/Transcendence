namespace Transcendence.Data.Models.Auth;

public class AdminAuditEvent
{
    public Guid Id { get; set; }
    public Guid? ActorUserAccountId { get; set; }
    public string? ActorEmail { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? TargetType { get; set; }
    public string? TargetId { get; set; }
    public string? RequestId { get; set; }
    public bool IsSuccess { get; set; }
    public string? MetadataJson { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
