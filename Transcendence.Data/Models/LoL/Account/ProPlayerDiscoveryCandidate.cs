namespace Transcendence.Data.Models.LoL.Account;

public class ProPlayerDiscoveryCandidate
{
    public Guid Id { get; set; }
    public string Source { get; set; } = string.Empty;
    public string ExternalId { get; set; } = string.Empty;
    public string ProName { get; set; } = string.Empty;
    public string? TeamName { get; set; }
    public string? Role { get; set; }
    public string? SoloQueueIds { get; set; }
    public string Status { get; set; } = "pending";
    public Guid? ApprovedTrackedProSummonerId { get; set; }
    public DateTime FirstSeenAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime LastSeenAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ReviewedAtUtc { get; set; }
}
