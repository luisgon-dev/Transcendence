using Microsoft.EntityFrameworkCore;
using Transcendence.Data;

namespace Transcendence.Service.Core.Services.Jobs;

public class BackfillMatchPlatformRegionJob(
    TranscendenceContext db,
    ILogger<BackfillMatchPlatformRegionJob> logger)
{
    private const int BatchSize = 1000;

    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        var totalUpdated = 0;

        while (!ct.IsCancellationRequested)
        {
            var updated = await db.Database.ExecuteSqlRawAsync("""
                UPDATE "Matches"
                SET "PlatformRegion" = SPLIT_PART("MatchId", '_', 1)
                WHERE "Id" IN (
                    SELECT "Id" FROM "Matches"
                    WHERE "PlatformRegion" IS NULL
                      AND "MatchId" IS NOT NULL
                      AND POSITION('_' IN "MatchId") > 0
                    LIMIT {0}
                )
                """, BatchSize, ct);

            if (updated == 0)
                break;

            totalUpdated += updated;
            logger.LogInformation(
                "Backfilled PlatformRegion for {BatchCount} matches ({TotalCount} total so far).",
                updated,
                totalUpdated);
        }

        if (totalUpdated > 0)
            logger.LogInformation("PlatformRegion backfill complete: updated {TotalCount} matches.", totalUpdated);
        else
            logger.LogDebug("PlatformRegion backfill: no matches needed updating.");
    }
}
