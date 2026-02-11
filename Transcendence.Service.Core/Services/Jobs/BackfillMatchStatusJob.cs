using Hangfire;
using Microsoft.EntityFrameworkCore;
using Transcendence.Data;
using Transcendence.Data.Models.LoL.Match;

namespace Transcendence.Service.Core.Services.Jobs;

/// <summary>
/// One-time backfill job to fix matches that were fully ingested but left with
/// Status = Unfetched due to a bug in GetMatchDetailsAsync (which never set
/// Status or FetchedAt). Matches with participants are definitively "fetched
/// successfully" since participants are only created after a successful API call.
/// </summary>
[DisableConcurrentExecution(timeoutInSeconds: 10 * 60)]
public class BackfillMatchStatusJob(
    TranscendenceContext db,
    ILogger<BackfillMatchStatusJob> logger)
{
    private const int BatchSize = 500;

    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var totalFixed = 0;

        logger.LogInformation("[BackfillMatchStatus] Starting backfill of unfetched matches with participant data.");

        while (!ct.IsCancellationRequested)
        {
            var batch = await db.Matches
                .Where(m => m.Status == FetchStatus.Unfetched && m.Participants.Any())
                .OrderBy(m => m.Id)
                .Take(BatchSize)
                .ToListAsync(ct);

            if (batch.Count == 0)
                break;

            foreach (var match in batch)
            {
                match.Status = FetchStatus.Success;
                match.FetchedAt = now;
            }

            await db.SaveChangesAsync(ct);
            totalFixed += batch.Count;

            logger.LogInformation(
                "[BackfillMatchStatus] Fixed batch of {BatchCount} matches ({TotalFixed} total so far).",
                batch.Count, totalFixed);
        }

        logger.LogInformation(
            "[BackfillMatchStatus] Backfill complete. Fixed {TotalFixed} matches total.",
            totalFixed);
    }
}
