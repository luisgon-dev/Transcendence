namespace Transcendence.Service.Core.Services.Analytics.Models;

public class ChampionAnalyticsComputeOptions
{
    public int MinimumGamesRequired { get; set; } = 100;
    public int EarlyPatchMinimumGamesRequired { get; set; } = 40;
    public int EarlyPatchWindowHours { get; set; } = 72;
}
