using Transcendence.Data.Models.LoL.Account;

namespace Transcendence.Data.Models.LoL.Match;

public class Match
{
    public Guid Id { get; set; }
    public string? MatchId { get; set; }
    public long MatchDate { get; set; }
    public int Duration { get; set; }
    public string? Patch { get; set; }
    public string? QueueType { get; set; }
    public string? EndOfGameResult { get; set; }

    public ICollection<MatchDetail> MatchDetails { get; set; } = new List<MatchDetail>();
    public List<Summoner> Summoners { get; set; } = [];
    public ICollection<MatchSummoner> MatchSummoners { get; set; } = new List<MatchSummoner>();
}