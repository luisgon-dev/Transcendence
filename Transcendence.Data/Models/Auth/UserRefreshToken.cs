namespace Transcendence.Data.Models.Auth;

public class UserRefreshToken
{
    public Guid Id { get; set; }
    public Guid UserAccountId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    public string? ReplacedByTokenHash { get; set; }
    public UserAccount UserAccount { get; set; } = null!;
}
