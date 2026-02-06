using Camille.Enums;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Transcendence.Data;
using Transcendence.Data.Models.LoL.Match;
using Transcendence.Service.Core.Services.Jobs.Configuration;
using Transcendence.Service.Core.Services.RiotApi.Interfaces;

namespace Transcendence.Service.Core.Services.Jobs;

[DisableConcurrentExecution(timeoutInSeconds: 10 * 60)]
public class RetryFailedMatchesJob(
    TranscendenceContext context,
    IMatchService matchService,
    IOptions<RetryFailedMatchesJobOptions> options,
    ILogger<RetryFailedMatchesJob> logger)
{
    public async Task Execute(CancellationToken cancellationToken)
    {
        var jobOptions = options.Value;
        var maxMatchesPerRun = Math.Max(1, jobOptions.MaxMatchesPerRun);
        var minimumMinutesSinceAttempt = Math.Max(1, jobOptions.MinimumMinutesSinceLastAttempt);

        // Find matches with TemporaryFailure that haven't been attempted recently.
        var cutoff = DateTime.UtcNow.AddMinutes(-minimumMinutesSinceAttempt);
        var failedMatches = await context.Matches
            .IgnoreQueryFilters() // Include PermanentlyUnfetchable for complete view
            .Where(m => m.Status == FetchStatus.TemporaryFailure && m.LastAttemptAt < cutoff)
            .OrderBy(m => m.LastAttemptAt)
            .Take(maxMatchesPerRun)
            .ToListAsync(cancellationToken);

        logger.LogInformation("Retrying {Count} failed matches", failedMatches.Count);

        foreach (var match in failedMatches)
        {
            if (string.IsNullOrWhiteSpace(match.MatchId))
            {
                logger.LogWarning("Skipping failed-match retry record {MatchEntityId} due to empty MatchId.", match.Id);
                continue;
            }

            try
            {
                var regionalRoute = ResolveRegionalRoute(match.MatchId);
                await matchService.FetchMatchWithRetryAsync(match.MatchId, regionalRoute.ToString(), cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed retry execution for match {MatchId}", match.MatchId);
            }
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
