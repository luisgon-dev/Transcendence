namespace Transcendence.Service.Core.Services.Jobs.Configuration;

public class RuneSelectionIntegrityBackfillJobOptions
{
    public int BatchSize { get; set; } = 250;
    public int MaxBatchesPerRun { get; set; } = 10;
}
