namespace Transcendence.Service.Core.Services.Jobs.Configuration;

public class MultiRegionIngestionOptions
{
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// When true, the discovery producers' fallback candidate selection skips summoners never seen
    /// active (LastActiveAtUtc is null — the large inert tail of MinValue stubs), refreshing
    /// recently-active players instead of paging the stubs oldest-first by UpdatedAt. Backed by the
    /// partial index IX_Summoners_Region_UpdatedAt_Active. Disable with
    /// Jobs__MultiRegionIngestion__PreferActiveSummoners=false.
    /// </summary>
    public bool PreferActiveSummoners { get; set; } = true;

    public int MaxConcurrentRegionBootstraps { get; set; } = 3;
    public int HighEloRefreshIntervalHours { get; set; } = 12;
    public List<RegionConfig> Regions { get; set; } = [];
}

public class RegionConfig
{
    public string Region { get; set; } = "NA1";
    public bool Enabled { get; set; } = true;
    public double Weight { get; set; } = 1.0;
    public int ChallengerSeedCount { get; set; } = 50;
}
