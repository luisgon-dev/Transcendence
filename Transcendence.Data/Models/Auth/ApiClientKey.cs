namespace Transcendence.Data.Models.Auth;

public class ApiClientKey
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public required string KeyHash { get; set; }
    public required string KeyPrefix { get; set; }
    public bool IsRevoked { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ExpiresAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
    public DateTime? RevokedAt { get; set; }
}
