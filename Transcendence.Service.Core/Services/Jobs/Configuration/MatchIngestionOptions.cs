namespace Transcendence.Service.Core.Services.Jobs.Configuration;

public class MatchIngestionOptions
{
    public int MatchIdsPageSize { get; set; } = 20;
    public int HighPriorityRankedPages { get; set; } = 2;
    public int HighPriorityAllModesHeadPages { get; set; } = 2;
    public int HighPriorityNonRankedBackfillMaxPages { get; set; } = 40;
    public int LowPriorityRankedPages { get; set; } = 1;
    public int LowPriorityAllModesHeadPages { get; set; } = 1;
    public int LowPriorityNonRankedBackfillMaxPages { get; set; } = 4;
}
