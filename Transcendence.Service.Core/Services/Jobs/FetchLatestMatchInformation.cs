using Camille.Enums;
using Camille.RiotGames;
using Microsoft.EntityFrameworkCore;
using Transcendence.Data;
using Transcendence.Data.Models.LoL.Match;
using Transcendence.Data.Repositories.Interfaces;
using Transcendence.Service.Core.Services.RiotApi;
using Transcendence.Service.Core.Services.RiotApi.Interfaces;
using Transcendence.Service.Core.Services.StaticData.Interfaces;

namespace Transcendence.Service.Core.Services.Jobs;

// ReSharper disable once ClassNeverInstantiated.Global
public class FetchLatestMatchInformation(
    RiotGamesApi riotGamesApi,
    TranscendenceContext context,
    IMatchService matchService,
    IMatchRepository matchRepository,
    IStaticDataService staticDataService,
    ILogger<FetchLatestMatchInformation> logger)
{
    public async Task Execute(CancellationToken stoppingToken)
    {
        // Ensure static data (latest patch) is up to date before fetching matches.
        await staticDataService.UpdateStaticDataAsync(stoppingToken);

        // TODO: prioritize by freshness/activity when this job is wired back into recurring scheduling.
        var summoners = await context.Summoners
            .AsNoTracking()
            .ToListAsync(stoppingToken);

        foreach (var summoner in summoners)
        {
            if (!PlatformRouteParser.TryParse(summoner.PlatformRegion, out var platformRoute))
            {
                logger.LogWarning(
                    "Skipping summoner {SummonerId}. Invalid platform route value: {PlatformRegion}",
                    summoner.Id,
                    summoner.PlatformRegion);
                continue;
            }

            if (string.IsNullOrWhiteSpace(summoner.Puuid))
            {
                logger.LogWarning("Skipping summoner {SummonerId}. Missing PUUID.", summoner.Id);
                continue;
            }

            IReadOnlyList<string> pendingMatchIds;
            try
            {
                pendingMatchIds = await FetchPendingMatchIdsAsync(platformRoute, summoner.Puuid, stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error fetching match IDs for summoner {SummonerId}", summoner.Id);
                continue;
            }

            if (pendingMatchIds.Count == 0)
                continue;

            var matchesToPersist = new List<Match>(pendingMatchIds.Count);
            foreach (var matchId in pendingMatchIds)
            {
                Match? match;
                try
                {
                    match = await matchService.GetMatchDetailsAsync(matchId, platformRoute.ToRegional(),
                        platformRoute, stoppingToken);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error fetching match information for match {MatchId}", matchId);
                    continue;
                }

                if (match == null)
                {
                    logger.LogInformation("Match {MatchId} failed to fetch", matchId);
                    continue;
                }

                matchesToPersist.Add(match);
                logger.LogInformation("Fetched match information for match {MatchId}", matchId);
            }

            await PersistMatchesAsync(matchesToPersist, summoner.Id, stoppingToken);
        }
    }

    private async Task<IReadOnlyList<string>> FetchPendingMatchIdsAsync(
        PlatformRoute platformRoute,
        string puuid,
        CancellationToken ct)
    {
        var matchIds = await riotGamesApi.MatchV5()
            .GetMatchIdsByPUUIDAsync(platformRoute.ToRegional(), puuid, 20, null,
                null, null, null, null, ct);

        var dedupedMatchIds = matchIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (dedupedMatchIds.Length == 0)
            return Array.Empty<string>();

        var existingMatchIds = await matchRepository.GetExistingMatchIdsAsync(dedupedMatchIds, ct);
        return dedupedMatchIds.Where(id => !existingMatchIds.Contains(id)).ToArray();
    }

    private async Task PersistMatchesAsync(
        IReadOnlyList<Match> matches,
        Guid summonerId,
        CancellationToken ct)
    {
        if (matches.Count == 0)
            return;

        try
        {
            foreach (var match in matches)
                await matchRepository.AddMatchAsync(match, ct);

            await context.SaveChangesAsync(ct);
            return;
        }
        catch (DbUpdateException ex) when (MatchPersistenceErrorClassifier.IsDuplicateMatchIdViolation(ex))
        {
            context.ChangeTracker.Clear();
            logger.LogInformation(
                "Detected duplicate match insert while persisting {Count} matches for summoner {SummonerId}. Falling back to per-match persistence.",
                matches.Count,
                summonerId);
        }
        catch (Exception ex)
        {
            context.ChangeTracker.Clear();
            logger.LogError(
                ex,
                "Unexpected error while persisting {Count} matches for summoner {SummonerId}. Falling back to per-match persistence.",
                matches.Count,
                summonerId);
        }

        foreach (var match in matches)
        {
            try
            {
                await matchRepository.AddMatchAsync(match, ct);
                await context.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (MatchPersistenceErrorClassifier.IsDuplicateMatchIdViolation(ex))
            {
                context.ChangeTracker.Clear();
            }
            catch (Exception ex)
            {
                context.ChangeTracker.Clear();
                logger.LogError(ex, "Error persisting match {MatchId} for summoner {SummonerId}", match.MatchId,
                    summonerId);
            }
        }
    }

}
