namespace Transcendence.Service.Core.Services.Jobs.Configuration;

public class FullHistoryBackfillJobOptions
{
    public bool Enabled { get; set; } = true;
    public int PageSize { get; set; } = 100;
    public int MaxPagesPerRun { get; set; } = 5;
    public int MaxFailureRetriesPerRun { get; set; } = 25;

    // Match-V5 matchlists are documented for the service lifetime that started on 2021-06-16 UTC.
    public long MinimumMatchStartEpochSeconds { get; set; } = 1623801600;
}
