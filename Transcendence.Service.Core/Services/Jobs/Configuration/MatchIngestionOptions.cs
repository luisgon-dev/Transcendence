namespace Transcendence.Service.Core.Services.Jobs.Configuration;

public class MatchIngestionOptions
{
    public int MatchIdsPageSize { get; set; } = 100;
    public int HighPriorityRankedPages { get; set; } = 2;
    public int HighPriorityAllModesHeadPages { get; set; } = 2;
    public int HighPriorityNonRankedBackfillMaxPages { get; set; } = 40;
    public int LowPriorityRankedPages { get; set; } = 1;
    public int LowPriorityAllModesHeadPages { get; set; } = 1;
    public int LowPriorityNonRankedBackfillMaxPages { get; set; } = 4;

    // Analytics ingestion only needs each summoner's RECENT matches to discover new games — older games
    // are already persisted. Clamp the match-id fetch window to this many days so a summoner refresh does
    // not re-scan weeks of history (the active patch can be many days old). Captures the current build +
    // an in-progress rollover.
    public int AnalyticsRecentWindowDays { get; set; } = 4;
}
