namespace Transcendence.Service.Core.Services.Analytics.Models;

public sealed class PrecomputedAnalyticsOptions
{
    public int MatchupChampionBatchSize { get; set; } = 16;
    public int CommandTimeoutSeconds { get; set; } = 120;
}
