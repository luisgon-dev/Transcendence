namespace Transcendence.Service.Core.Services.Analytics.Models;

public class ChampionAnalyticsComputeOptions
{
    public int MinimumGamesRequired { get; set; } = 100;
    public int MaturingPatchMinimumGamesRequired { get; set; } = 70;
    public int EarlyPatchMinimumGamesRequired { get; set; } = 40;
    public int BootstrapPatchMinimumGamesRequired { get; set; } = 10;
    public int BootstrapWindowHours { get; set; } = 24;
    public int ProvisionalWindowHours { get; set; } = 96;
    public int MaturingWindowHours { get; set; } = 240;
}
