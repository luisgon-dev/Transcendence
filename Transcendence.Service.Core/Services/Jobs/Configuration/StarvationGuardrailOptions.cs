namespace Transcendence.Service.Core.Services.Jobs.Configuration;

public class StarvationGuardrailOptions
{
    public bool Enabled { get; set; } = true;
    public int MaxEligibleDeferAgeMinutes { get; set; } = 360;
    public int CatchUpWindowMinutes { get; set; } = 12;
    public int CatchUpCooldownMinutes { get; set; } = 20;
    public int ForcedCatchUpQueueTargetFloor { get; set; } = 1;
    public double ForcedCatchUpCandidateBurstMultiplier { get; set; } = 1.5d;
    public int ForcedCatchUpMaxCandidateHardCap { get; set; } = 500;
}
