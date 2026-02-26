namespace Transcendence.Data.Models.Auth;

public class UserRole
{
    public Guid UserAccountId { get; set; }
    public string Role { get; set; } = string.Empty;
    public DateTime GrantedAtUtc { get; set; } = DateTime.UtcNow;
    public string? GrantedBy { get; set; }
    public UserAccount UserAccount { get; set; } = null!;
}
