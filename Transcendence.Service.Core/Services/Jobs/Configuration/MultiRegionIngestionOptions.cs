namespace Transcendence.Service.Core.Services.Jobs.Configuration;

public class MultiRegionIngestionOptions
{
    public bool Enabled { get; set; } = true;
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
