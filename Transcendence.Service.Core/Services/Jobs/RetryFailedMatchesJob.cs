using Camille.Enums;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Transcendence.Data;
using Transcendence.Data.Models.LoL.Match;
using Transcendence.Data.Repositories.Interfaces;
using Transcendence.Service.Core.Services.Jobs.Configuration;
using Transcendence.Service.Core.Services.RiotApi.Interfaces;

namespace Transcendence.Service.Core.Services.Jobs;

[DisableConcurrentExecution(timeoutInSeconds: 10 * 60)]
public class RetryFailedMatchesJob(
    TranscendenceContext context,
    IMatchService matchService,
    IOptions<RetryFailedMatchesJobOptions> options,
    IOptions<ChampionAnalyticsIngestionJobOptions> analyticsIngestionOptions,
    IRefreshLockRepository refreshLockRepository,
    ILogger<RetryFailedMatchesJob> logger)
{
    public async Task Execute(CancellationToken cancellationToken)
    {
        if (analyticsIngestionOptions.Value.PauseWhenApiPriorityRefreshActive &&
            await refreshLockRepository.AnyActiveByPrefixAsync(RefreshLockKeys.ApiPriorityRefreshPrefix,
                cancellationToken))
        {
            logger.LogInformation("RetryFailedMatches skipped: active high-priority API refresh demand detected.");
            return;
        }

        var jobOptions = options.Value;
        var maxMatchesPerRun = Math.Max(1, jobOptions.MaxMatchesPerRun);
        var minimumMinutesSinceAttempt = Math.Max(1, jobOptions.MinimumMinutesSinceLastAttempt);

        await ReviveRateGateMisclassifiedAsync(jobOptions, cancellationToken);

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

    // One-time backlog drain: matches wrongly flipped to PermanentlyUnfetchable by the old
    // rate-gate-as-failure bug carried the thrown message "...rate gate exhausted...". Revive a
    // bounded batch of those to TemporaryFailure so the corrected fetch path re-evaluates them.
    // Genuine 404/gone rows (message "Riot API returned null …") do NOT match and are left terminal.
    private async Task ReviveRateGateMisclassifiedAsync(
        RetryFailedMatchesJobOptions jobOptions, CancellationToken cancellationToken)
    {
        var revivePerRun = Math.Max(0, jobOptions.RevivePermanentlyUnfetchablePerRun);
        if (revivePerRun == 0)
            return;

        var revived = await context.Matches
            .IgnoreQueryFilters()
            .Where(m => m.Status == FetchStatus.PermanentlyUnfetchable
                        && m.LastErrorMessage != null
                        && m.LastErrorMessage.Contains("rate gate exhausted"))
            .OrderBy(m => m.LastAttemptAt)
            .Take(revivePerRun)
            .ToListAsync(cancellationToken);

        if (revived.Count == 0)
            return;

        foreach (var match in revived)
        {
            match.Status = FetchStatus.TemporaryFailure;
            match.RetryCount = 0;
            match.LastErrorMessage = "Revived from rate-gate misclassification; will retry.";
        }

        await context.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "Revived {Count} match(es) wrongly marked unfetchable under rate pressure.", revived.Count);
    }

    private static RegionalRoute ResolveRegionalRoute(string matchId)
    {
        var prefix = matchId.Split('_')[0].ToUpperInvariant();
        if (Enum.TryParse<PlatformRoute>(prefix, true, out var platformRoute))
            return platformRoute.ToRegional();

        return RegionalRoute.AMERICAS;
    }
}
