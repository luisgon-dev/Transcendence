namespace Transcendence.Data.Models.Auth;

public class UserPreferences
{
    public Guid UserAccountId { get; set; }
    public string? PreferredRegion { get; set; }
    public string? PreferredRankTier { get; set; }
    public bool LivePollingEnabled { get; set; } = true;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public UserAccount UserAccount { get; set; } = null!;
}
