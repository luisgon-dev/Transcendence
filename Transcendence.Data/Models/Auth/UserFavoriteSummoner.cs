namespace Transcendence.Data.Models.Auth;

public class UserFavoriteSummoner
{
    public Guid Id { get; set; }
    public Guid UserAccountId { get; set; }
    public string SummonerPuuid { get; set; } = string.Empty;
    public string PlatformRegion { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public UserAccount UserAccount { get; set; } = null!;
}
