using Transcendence.Data.Models.Tft.Static;

namespace Transcendence.Data.Models.Tft.Match;

public class TftMatch
{
    public Guid Id { get; set; }
    public string MatchId { get; set; } = string.Empty;
    public long MatchDate { get; set; }
    public int DurationSeconds { get; set; }
    public string? Patch { get; set; }
    public int? SetNumber { get; set; }
    public string? SetCoreName { get; set; }
    public string? TftGameType { get; set; }
    public string? GameVariation { get; set; }
    public string? QueueId { get; set; }
    public string PlatformRegion { get; set; } = string.Empty;
    public TftFetchStatus Status { get; set; } = TftFetchStatus.Unfetched;
    public DateTime? FetchedAtUtc { get; set; }
    public DateTime? LastAttemptAtUtc { get; set; }
    public string? LastErrorMessage { get; set; }

    public ICollection<TftMatchParticipant> Participants { get; set; } = new List<TftMatchParticipant>();
    public TftSet? Set { get; set; }
}
