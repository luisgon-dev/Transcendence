namespace Transcendence.Data.Models.LoL.Account;

public class TrackedProSummoner
{
    public Guid Id { get; set; }
    public string Puuid { get; set; } = string.Empty;
    public string PlatformRegion { get; set; } = string.Empty;
    public string? GameName { get; set; }
    public string? TagLine { get; set; }
    public string? ProName { get; set; }
    public string? TeamName { get; set; }
    public bool IsPro { get; set; } = true;
    public bool IsHighEloOtp { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
