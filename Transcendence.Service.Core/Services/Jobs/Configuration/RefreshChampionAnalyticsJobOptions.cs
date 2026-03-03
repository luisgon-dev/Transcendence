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
    public int NewPatchRampHours { get; set; } = 48;
    public int RampChampionsPerRoleToPreWarm { get; set; } = 20;
    public int RampAdaptiveNewMatchesThreshold { get; set; } = 100;
    public int RampAdaptiveLookbackMinutes { get; set; } = 10;
    public int RampMinimumRefreshIntervalMinutes { get; set; } = 10;
    public int RampForceRefreshAfterHours { get; set; } = 2;
}
