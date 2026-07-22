namespace Transcendence.Service.Core.Services.Jobs.Configuration;

/// <summary>
/// Operational policy for Match-V5 detail retrieval. These values track Riot retention behavior and
/// retry tolerance, so operators can tune them without changing ingestion code.
/// </summary>
public sealed class MatchFetchOptions
{
    /// <summary>Maximum known match age that Riot Match-V5 is expected to retain.</summary>
    public int RetentionDays { get; set; } = 730;

    /// <summary>Number of genuine fetch failures before a match becomes terminally unavailable.</summary>
    public int MaxRetryAttempts { get; set; } = 5;
}
