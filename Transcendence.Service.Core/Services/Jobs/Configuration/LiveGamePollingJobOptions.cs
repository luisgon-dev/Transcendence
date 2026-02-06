namespace Transcendence.Service.Core.Services.Jobs.Configuration;

public class LiveGamePollingJobOptions
{
    public int MaxTrackedSummonersPerRun { get; set; } = 30;
    public int MaxRiotRequestsPerRun { get; set; } = 30;
    public bool PollOnlyFavoriteSummoners { get; set; } = true;
    public bool RespectUserLivePollingPreference { get; set; } = true;
    public bool PauseWhileChampionAnalyticsUnavailable { get; set; } = true;
}
