namespace Transcendence.Service.Core.Services.Jobs.Configuration;

public class SummonerBootstrapOptions
{
    public bool Enabled { get; set; } = true;
    public string PlatformRegion { get; set; } = "NA1";
    public int ChallengerSeedCount { get; set; } = 10;
    public int LockMinutes { get; set; } = 10;
}

