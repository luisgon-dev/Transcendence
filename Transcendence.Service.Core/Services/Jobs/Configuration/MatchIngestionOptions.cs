namespace Transcendence.Service.Core.Services.Jobs.Configuration;

public class MatchIngestionOptions
{
    public int MatchIdsPageSize { get; set; } = 100;
    // Bounded parallelism for match-detail fetches within a page. Camille still enforces the
    // per-region rate limit, so this lets the limiter stay saturated instead of the app
    // serializing one await at a time.
    public int MatchFetchConcurrency { get; set; } = 4;
    public int HighPriorityRankedPages { get; set; } = 2;
    public int HighPriorityAllModesHeadPages { get; set; } = 2;
    public int HighPriorityNonRankedBackfillMaxPages { get; set; } = 40;
    public int LowPriorityRankedPages { get; set; } = 1;
    public int LowPriorityAllModesHeadPages { get; set; } = 1;
    public int LowPriorityNonRankedBackfillMaxPages { get; set; } = 4;
}
