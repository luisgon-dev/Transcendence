using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Transcendence.Data;
using Transcendence.Data.Models.LoL.Match;
using Transcendence.Data.Repositories.Interfaces;
using Transcendence.Service.Core.Services.Jobs.Configuration;
using Transcendence.Service.Core.Services.RiotApi;

namespace Transcendence.Service.Core.Services.Jobs;

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

        var candidateMatchIds = await query
            .Where(m => !db.MatchParticipantTimelineSnapshots.Any(s =>
                s.MatchId == m.Id &&
                s.MinuteMark == minuteMark))
            .Where(m => !db.MatchTimelineFetchStates.Any(s =>
                s.MatchId == m.Id &&
                s.Status == MatchTimelineFetchStatus.PermanentlyFailed))
            .OrderByDescending(m => m.MatchDate)
            .ThenByDescending(m => m.MatchId)
            .Select(m => m.MatchId!)
            .Take(take)
            .ToListAsync(ct);

        if (candidateMatchIds.Count == 0)
        {
            logger.LogInformation("[TimelineBackfill] No missing ranked timeline rows found in current scope.");
            return;
        }

        var enqueued = 0;
        foreach (var matchId in candidateMatchIds)
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
                job => job.IngestMatchTimelineAsync(matchId, CancellationToken.None));
            enqueued++;
        }

        logger.LogInformation(
            "[TimelineBackfill] Enqueued {EnqueuedCount}/{CandidateCount} timeline ingestion jobs (patch scope: {PatchScope}).",
            enqueued,
            candidateMatchIds.Count,
            options.BackfillCurrentPatchOnly ? activePatch ?? "current-patch-only(no-active-patch)" : "all-patches");
    }
}
