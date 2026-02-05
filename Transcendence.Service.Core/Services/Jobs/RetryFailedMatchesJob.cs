using Microsoft.EntityFrameworkCore;
using Camille.Enums;
using Transcendence.Data;
using Transcendence.Data.Models.LoL.Match;
using Transcendence.Service.Core.Services.RiotApi.Interfaces;

namespace Transcendence.Service.Core.Services.Jobs;

public class RetryFailedMatchesJob(
    TranscendenceContext context,
    IMatchService matchService,
    ILogger<RetryFailedMatchesJob> logger)
{
    public async Task Execute(CancellationToken cancellationToken)
    {
        // Find matches with TemporaryFailure that haven't been attempted in last 10 minutes
        var cutoff = DateTime.UtcNow.AddMinutes(-10);
        var failedMatches = await context.Matches
            .IgnoreQueryFilters() // Include PermanentlyUnfetchable for complete view
            .Where(m => m.Status == FetchStatus.TemporaryFailure && m.LastAttemptAt < cutoff)
            .Take(100) // Batch size to prevent overwhelming API
            .ToListAsync(cancellationToken);

        logger.LogInformation("Retrying {Count} failed matches", failedMatches.Count);

        foreach (var match in failedMatches)
        {
            var regionalRoute = ResolveRegionalRoute(match.MatchId!);
            await matchService.FetchMatchWithRetryAsync(match.MatchId!, regionalRoute.ToString(), cancellationToken);
        }
    }

    private static RegionalRoute ResolveRegionalRoute(string matchId)
    {
        var prefix = matchId.Split('_')[0].ToUpperInvariant();
        if (Enum.TryParse<PlatformRoute>(prefix, true, out var platformRoute))
            return platformRoute.ToRegional();

        return RegionalRoute.AMERICAS;
    }
}
