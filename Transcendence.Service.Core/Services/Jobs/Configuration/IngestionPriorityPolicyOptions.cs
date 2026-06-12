namespace Transcendence.Service.Core.Services.Jobs.Configuration;

public class IngestionPriorityPolicyOptions
{
    public double PatchRelevanceWeight { get; set; } = 8d;
    public double StalenessWeight { get; set; } = 4d;
    public double FavoriteWeight { get; set; } = 2d;
    public int StalenessSaturationMinutes { get; set; } = 180;

    // Activity recency (Summoner.LastActiveAtUtc): a summoner who recently played is the best
    // predictor of new matches per refresh, so this carries the heaviest weight. The signal decays
    // linearly from ~1 (just played) to 0 at ActivitySaturationMinutes ago; unknown activity = 0.
    public double ActivityWeight { get; set; } = 6d;
    public int ActivitySaturationMinutes { get; set; } = 1440;
}
