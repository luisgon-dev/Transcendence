namespace Transcendence.Service.Core.Services.Analytics.Models;

public sealed class PrecomputedAnalyticsOptions
{
    public int MatchupSourceMatchBatchSize { get; set; } = 250;
    public int MatchupChampionBatchSize { get; set; } = 8;
    public int CommandTimeoutSeconds { get; set; } = 45;
    public int MaxGenerationResumeAttempts { get; set; } = 3;
    public int RetainedMatchupGenerations { get; set; } = 2;
}
