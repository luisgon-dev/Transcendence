using Transcendence.Data.Models.LoL.Account;

namespace Transcendence.Data.Models.LoL.Match;

public enum FetchStatus
{
    Unfetched = 0,
    Success = 1,
    TemporaryFailure = 2,
    PermanentlyUnfetchable = 3,
    OutsideRetentionWindow = 4
}

public class Match
{
    public Guid Id { get; set; }
    public string? MatchId { get; set; }
    public long MatchDate { get; set; }
    public int Duration { get; set; }
    public string? Patch { get; set; }
    public int QueueId { get; set; }
    public string? QueueFamily { get; set; }
    public string? QueueType { get; set; }
    public string? EndOfGameResult { get; set; }

    // Fetch metadata
    public FetchStatus Status { get; set; } = FetchStatus.Unfetched;
    public int RetryCount { get; set; } = 0;
    public DateTime? FetchedAt { get; set; }
    public DateTime? LastAttemptAt { get; set; }
    public string? LastErrorMessage { get; set; }

    public List<Summoner> Summoners { get; set; } = [];
    public ICollection<MatchParticipant> Participants { get; set; } = new List<MatchParticipant>();
    public ICollection<MatchBan> Bans { get; set; } = new List<MatchBan>();
    public ICollection<MatchParticipantTimelineSnapshot> TimelineSnapshots { get; set; } =
        new List<MatchParticipantTimelineSnapshot>();
    public MatchTimelineFetchState? TimelineFetchState { get; set; }
}
