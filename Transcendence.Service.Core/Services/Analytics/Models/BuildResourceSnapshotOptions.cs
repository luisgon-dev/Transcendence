namespace Transcendence.Service.Core.Services.Analytics.Models;

public sealed class BuildResourceSnapshotOptions
{
    public int MatchBatchSize { get; set; } = 500;
    public int CommandTimeoutSeconds { get; set; } = 120;
}
