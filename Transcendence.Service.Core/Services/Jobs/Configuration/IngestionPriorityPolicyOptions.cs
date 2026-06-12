namespace Transcendence.Service.Core.Services.Jobs.Configuration;

public class IngestionPriorityPolicyOptions
{
    public double PatchRelevanceWeight { get; set; } = 8d;
    public double StalenessWeight { get; set; } = 4d;
    public double FavoriteWeight { get; set; } = 2d;
    public int StalenessSaturationMinutes { get; set; } = 180;
}
