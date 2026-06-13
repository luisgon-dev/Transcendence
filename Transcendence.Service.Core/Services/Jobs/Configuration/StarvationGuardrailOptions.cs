namespace Transcendence.Service.Core.Services.Jobs.Configuration;

public class StarvationGuardrailOptions
{
    // DISABLED by default: the defer-age signal (oldest eligible summoner's UpdatedAt age) is
    // structurally unsatisfiable at this scale on the yield-limited personal Riot key — of ~4.1M
    // summoners only ~8.5k are <6h fresh, so the oldest is always months stale and the breach predicate
    // (>= MaxEligibleDeferAgeMinutes) is PERMANENTLY true → forced catch-up fired perpetually, bypassing
    // the API-priority pause and (with all-modes widening) grinding ancient history. Absolute age is the
    // wrong signal here; the adaptive throughput budget's coverage/velocity-driven CatchUp mode is the
    // legitimate bursting mechanism, and the cold-start override handles new regions. Re-enable only with
    // a delta/growth-based defer signal, not absolute age.
    public bool Enabled { get; set; }
    public int MaxEligibleDeferAgeMinutes { get; set; } = 360;
    public int CatchUpWindowMinutes { get; set; } = 12;
    public int CatchUpCooldownMinutes { get; set; } = 20;
    public int ForcedCatchUpQueueTargetFloor { get; set; } = 1;
    public double ForcedCatchUpCandidateBurstMultiplier { get; set; } = 1.5d;
    public int ForcedCatchUpMaxCandidateHardCap { get; set; } = 500;
}
