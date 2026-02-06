namespace Transcendence.Service.Core.Services.RiotApi.DTOs;

public class MatchHistoryResponse
{
    public List<MatchSummary> Matches { get; set; } = new();
}

public class MatchSummary
{
    public string MatchId { get; set; } = string.Empty;
    public DateTime MatchDate { get; set; }
    public int Duration { get; set; }
    public string QueueType { get; set; } = string.Empty;
    public string Result { get; set; } = string.Empty; // Win/Loss

    // Participant stats
    public int ChampionId { get; set; }
    public string ChampionName { get; set; } = string.Empty;
    public int Kills { get; set; }
    public int Deaths { get; set; }
    public int Assists { get; set; }

    // Data freshness
    public DataAgeMetadata DataAge { get; set; } = new();
}
