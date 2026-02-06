namespace Transcendence.Service.Core.Services.Jobs.Configuration;

public class RefreshChampionAnalyticsJobOptions
{
    public int PopularChampionsTakeCount { get; set; } = 100;
    public int ChampionsPerRoleToPreWarm { get; set; } = 12;
    public int AdaptiveNewMatchesThreshold { get; set; } = 500;
    public int AdaptiveLookbackMinutes { get; set; } = 30;
    public int MinimumRefreshIntervalMinutes { get; set; } = 120;
    public int ForceRefreshAfterHours { get; set; } = 24;
    public bool EnqueueIngestionWhenNoPopularChampions { get; set; } = true;
}
