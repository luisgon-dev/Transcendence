namespace Transcendence.Data.Models.Auth;

public class UserRiotAccount
{
    public Guid UserAccountId { get; set; }
    public string Puuid { get; set; } = string.Empty;
    public string GameName { get; set; } = string.Empty;
    public string TagLine { get; set; } = string.Empty;
    public string PlatformRegion { get; set; } = string.Empty;
    public DateTime LinkedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime VerifiedAtUtc { get; set; } = DateTime.UtcNow;
    public UserAccount UserAccount { get; set; } = null!;
}
