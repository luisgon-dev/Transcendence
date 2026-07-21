namespace Transcendence.Data.Models.Auth;

public class UserAccount
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string EmailNormalized { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginAtUtc { get; set; }

    /// <summary>Consecutive failed login attempts since the last success; drives per-account lockout.</summary>
    public int FailedLoginAttempts { get; set; }

    /// <summary>When set and in the future, logins for this account are refused regardless of source IP.</summary>
    public DateTime? LockoutUntilUtc { get; set; }

    public ICollection<UserRole> Roles { get; set; } = new List<UserRole>();
    public ICollection<UserRefreshToken> RefreshTokens { get; set; } = new List<UserRefreshToken>();
    public ICollection<UserPasswordResetToken> PasswordResetTokens { get; set; } = new List<UserPasswordResetToken>();
    public ICollection<UserFavoriteSummoner> FavoriteSummoners { get; set; } = new List<UserFavoriteSummoner>();
    public UserPreferences? Preferences { get; set; }
    public UserRiotAccount? RiotAccount { get; set; }
}
