using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Transcendence.Data;
using Transcendence.Data.Models.LoL.Match;
using Transcendence.Data.Repositories.Interfaces;
using Transcendence.Service.Core.Services.Jobs.Configuration;
using Transcendence.Service.Core.Services.RiotApi;

namespace Transcendence.Service.Core.Services.Jobs;

// No automatic retry: this job enqueues ingestion jobs as a side effect, so a retry after a mid-run
// failure would re-enqueue the same matches as duplicates. A failed run simply waits for the next cron.
[AutomaticRetry(Attempts = 0)]
[DisableConcurrentExecution(timeoutInSeconds: 10 * 60)]
public class MatchTimelineBackfillJob(
    TranscendenceContext db,
    IBackgroundJobClient backgroundJobClient,
    IOptions<TimelineIngestionOptions> timelineOptions,
    IOptions<ChampionAnalyticsIngestionJobOptions> analyticsIngestionOptions,
    IRefreshLockRepository refreshLockRepository,
    ILogger<MatchTimelineBackfillJob> logger)
{
    [Queue("refresh-low")]
    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        var options = timelineOptions.Value;
        if (!options.Enabled)
            return;

        if (options.PauseWhenApiPriorityRefreshActive &&
            await refreshLockRepository.AnyActiveByPrefixAsync(RefreshLockKeys.ApiPriorityRefreshPrefix, ct))
        {
            logger.LogInformation("[TimelineBackfill] Skipped: active high-priority API refresh demand detected.");
            return;
        }

        var take = Math.Max(1, options.BackfillBatchSize);
        var maxEnqueues = Math.Max(1, options.BackfillMaxEnqueuesPerRun);
        var minuteMark = Math.Max(1, options.MinuteMark);

        var activePatch = await db.Patches
            .AsNoTracking()
            .Where(p => p.IsActive)
            .Select(p => p.Version)
            .FirstOrDefaultAsync(ct);

        IQueryable<Match> query = db.Matches
            .AsNoTracking()
            .Where(m => m.Status == FetchStatus.Success)
            .Where(m => m.MatchId != null && m.MatchId != "")
            .Where(m => m.QueueId == QueueCatalog.RankedSoloDuoQueueId ||
                        (m.QueueId == 0 && m.QueueType == QueueCatalog.RankedSoloDuoQueueId.ToString()))
            .Where(m => m.Duration > 0);

        if (options.BackfillCurrentPatchOnly && !string.IsNullOrWhiteSpace(activePatch))
            query = query.Where(m => m.Patch == activePatch);

        var cooldownThreshold = DateTime.UtcNow.AddMinutes(-Math.Max(0, options.BackfillReattemptCooldownMinutes));

        var candidates = await query
            // Either never ingested (no snapshot at the anchor mark) or ingested under an older
            // schema that predates the ordered build-path tables, so re-ingest once to backfill them.
            // The cooldown keeps a stale-schema match that was just enqueued (LastAttemptAtUtc stamped
            // below) out of the candidate set until its ingestion job runs, so a high-throughput
            // backfill does not re-enqueue still-queued matches and waste Riot API budget on duplicates.
            .Where(m => !db.MatchParticipantTimelineSnapshots.Any(s =>
                            s.MatchId == m.Id &&
                            s.MinuteMark == minuteMark)
                        || db.MatchTimelineFetchStates.Any(s =>
                            s.MatchId == m.Id &&
                            s.Status == MatchTimelineFetchStatus.Success &&
                            s.SchemaVersion < MatchTimelineIngestionJob.CurrentTimelineSchemaVersion &&
                            (s.LastAttemptAtUtc == null || s.LastAttemptAtUtc < cooldownThreshold)))
            .Where(m => !db.MatchTimelineFetchStates.Any(s =>
                s.MatchId == m.Id &&
                s.Status == MatchTimelineFetchStatus.PermanentlyFailed))
            .OrderByDescending(m => m.MatchDate)
            .ThenByDescending(m => m.MatchId)
            .Select(m => new { m.Id, MatchId = m.MatchId! })
            .Take(take)
            .ToListAsync(ct);

        if (candidates.Count == 0)
        {
            logger.LogInformation("[TimelineBackfill] No missing ranked timeline rows found in current scope.");
            return;
        }

        var enqueuedMatchGuids = new List<Guid>(Math.Min(candidates.Count, maxEnqueues));
        var enqueued = 0;
        foreach (var candidate in candidates)
        {
            if (enqueued >= maxEnqueues)
                break;

            if (analyticsIngestionOptions.Value.PauseWhenApiPriorityRefreshActive &&
                await refreshLockRepository.AnyActiveByPrefixAsync(RefreshLockKeys.ApiPriorityRefreshPrefix, ct))
            {
                logger.LogInformation(
                    "[TimelineBackfill] Stopped early after {Count} enqueues due to active high-priority API refresh demand.",
                    enqueued);
                break;
            }

            backgroundJobClient.Enqueue<MatchTimelineIngestionJob>(
                job => job.IngestMatchTimelineAsync(candidate.MatchId, CancellationToken.None));
            enqueuedMatchGuids.Add(candidate.Id);
            enqueued++;
        }

        // Stamp the just-enqueued (stale-schema) matches as freshly attempted so the cooldown excludes
        // them from the next run until their ingestion job runs and advances SchemaVersion.
        // IgnoreQueryFilters: ExecuteUpdate cannot translate this entity's navigation-based query filter
        // (Match.Status) into a bulk UPDATE join, and would throw — failing the backfill after it has
        // already enqueued, which would retry and re-enqueue the same (un-stamped) matches as duplicates.
        if (enqueuedMatchGuids.Count > 0)
        {
            await db.MatchTimelineFetchStates
                .IgnoreQueryFilters()
                .Where(s => enqueuedMatchGuids.Contains(s.MatchId))
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.LastAttemptAtUtc, DateTime.UtcNow), ct);
        }

        logger.LogInformation(
            "[TimelineBackfill] Enqueued {EnqueuedCount}/{CandidateCount} timeline ingestion jobs (patch scope: {PatchScope}).",
            enqueued,
            candidates.Count,
            options.BackfillCurrentPatchOnly ? activePatch ?? "current-patch-only(no-active-patch)" : "all-patches");
    }
}
