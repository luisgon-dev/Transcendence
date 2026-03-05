namespace Transcendence.Service.Core.Services.Jobs.Configuration;

public class AdaptiveThroughputBudgetOptions
{
    public int VelocityLookbackMinutes { get; set; } = 30;
    public int HighPressureCooldownMinutes { get; set; } = 8;
    public int CatchUpHoldMinutes { get; set; } = 12;
    public int ModeSwitchCooldownMinutes { get; set; } = 4;
    public double CatchUpCoverageThreshold { get; set; } = 0.85d;
    public int CatchUpBacklogAgeMinutes { get; set; } = 45;
    public double CatchUpCandidatePressureThreshold { get; set; } = 1.1d;
    public double MinimumRecentVelocityPerHour { get; set; } = 12d;
    public double CatchUpQueueBurstMultiplier { get; set; } = 1.6d;
    public double CatchUpCandidateBurstMultiplier { get; set; } = 1.8d;
    public double HighPressureCandidateMultiplier { get; set; } = 0.25d;
    public int MaxQueueTargetHardCap { get; set; } = 40;
    public int MaxCandidateHardCap { get; set; } = 500;
}
