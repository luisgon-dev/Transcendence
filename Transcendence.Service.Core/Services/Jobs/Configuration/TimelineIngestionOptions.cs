namespace Transcendence.Service.Core.Services.Jobs.Configuration;

public class TimelineIngestionOptions
{
    public bool Enabled { get; set; } = true;
    public int MinuteMark { get; set; } = 15;
    /// <summary>
    /// Cadence (in minutes) for the multi-frame timeline curve. Snapshots are captured at
    /// each multiple of this value up to game length, plus the analytics <see cref="MinuteMark"/>.
    /// </summary>
    public int FrameIntervalMinutes { get; set; } = 2;
    public int MaxRetryAttempts { get; set; } = 4;
    public int BackfillBatchSize { get; set; } = 100;
    public int BackfillMaxEnqueuesPerRun { get; set; } = 50;
    public bool BackfillCurrentPatchOnly { get; set; } = true;
    public bool PauseWhenApiPriorityRefreshActive { get; set; } = true;
}
