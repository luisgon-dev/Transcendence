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
    public int BackfillBatchSize { get; set; } = 1500;
    public int BackfillMaxEnqueuesPerRun { get; set; } = 1500;
    public bool BackfillCurrentPatchOnly { get; set; } = true;
    public bool PauseWhenApiPriorityRefreshActive { get; set; } = true;

    /// <summary>
    /// A match already enqueued by the backfill is not re-selected for this many minutes (it stays
    /// stale-schema until its ingestion job runs). Without this, a high-throughput backfill re-selects
    /// still-queued matches every run and re-enqueues them, wasting Riot API budget on duplicates.
    /// </summary>
    public int BackfillReattemptCooldownMinutes { get; set; } = 60;
}
