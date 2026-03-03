namespace Transcendence.Service.Core.Services.Jobs.Configuration;

public class SummonerMaintenanceJobOptions
{
    public int MaxCandidateSummonersPerRun { get; set; } = 60;
    public int MaxRefreshJobsToQueuePerRun { get; set; } = 4;
    public int DataStaleAfterMinutes { get; set; } = 90;
    public int RefreshLockMinutes { get; set; } = 10;
    public bool PrioritizeFavoriteSummoners { get; set; } = true;
    public bool PauseWhenApiPriorityRefreshActive { get; set; } = true;
    public int NewPatchRampHours { get; set; } = 48;
    public int RampMaxCandidateSummonersPerRun { get; set; } = 250;
    public int RampMaxRefreshJobsToQueuePerRun { get; set; } = 12;
    public int RampDataStaleAfterMinutes { get; set; } = 30;
}
