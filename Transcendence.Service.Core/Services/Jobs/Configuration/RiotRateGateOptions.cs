namespace Transcendence.Service.Core.Services.Jobs.Configuration;

/// <summary>
/// Per-routing-region request-rate budget for Riot calls. Riot enforces the app rate limit per routing
/// value (subdomain — americas/europe/asia/sea for match-v5, na1/euw1/... for platform APIs), so the gate
/// keeps the system <b>under</b> each region's budget. That prevents Camille's own rate limiter from
/// queuing/parking requests (the saturation that stalled the consumers) and lets the regions' budgets be
/// used in parallel. Defaults sit just under a personal/dev-tier key (20 req/s, 100 req/2min per region).
/// </summary>
public class RiotRateGateOptions
{
    public bool Enabled { get; set; } = true;

    /// <summary>Bucket capacity / max burst per region. Keep below the 1s burst limit (20).</summary>
    public int BurstTokens { get; set; } = 16;

    /// <summary>Tokens replenished each <see cref="ReplenishPeriodSeconds"/>. With the period below this
    /// sets the sustained rate; default 3 per 4s = 45/min = 90 per 2 min (under the 100/2min limit).</summary>
    public int TokensPerPeriod { get; set; } = 3;

    public int ReplenishPeriodSeconds { get; set; } = 4;

    /// <summary>Max time a single call waits for a token before the gate rejects it (caller skips and the
    /// match is retried on a later refresh). Bounds the wait so a backed-up region can never park a
    /// consumer indefinitely.</summary>
    public int MaxWaitSeconds { get; set; } = 30;
}
