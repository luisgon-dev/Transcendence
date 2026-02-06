namespace Transcendence.Data.Models.LiveGame;

public class LiveGameSnapshot
{
    public Guid Id { get; set; }
    public Guid SummonerId { get; set; }
    public string Puuid { get; set; } = string.Empty;
    public string PlatformRegion { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string? GameId { get; set; }
    public DateTime ObservedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime NextPollAtUtc { get; set; }
}
