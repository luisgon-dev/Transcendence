using Camille.Enums;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Transcendence.Data;
using Transcendence.Data.Models.LoL.Match;
using Transcendence.Data.Repositories.Interfaces;
using Transcendence.Service.Core.Services.Jobs.Configuration;
using Transcendence.Service.Core.Services.Jobs.Interfaces;
using Transcendence.Service.Core.Services.RiotApi;

namespace Transcendence.Service.Core.Services.Jobs;

[DisableConcurrentExecution(timeoutInSeconds: 10 * 60)]
public class ChampionAnalyticsIngestionJob(
    TranscendenceContext db,
    ISummonerBootstrapService bootstrapService,
    IRefreshLockRepository refreshLockRepository,
    IBackgroundJobClient backgroundJobClient,
    IOptions<ChampionAnalyticsIngestionJobOptions> options,
    ILogger<ChampionAnalyticsIngestionJob> logger)
{
    private sealed record CandidateSummoner(
        string PlatformRegion,
        string GameName,
        string TagLine,
        DateTime UpdatedAt);

    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        var jobOptions = options.Value;

        await bootstrapService.EnsureSeededFromChallengerAsync(ct);

        var currentPatchInfo = await db.Patches
            .AsNoTracking()
            .Where(p => p.IsActive)
            .Select(p => new { p.Version, p.ReleaseDate })
            .FirstOrDefaultAsync(ct);

        if (currentPatchInfo == null || string.IsNullOrWhiteSpace(currentPatchInfo.Version))
        {
            logger.LogWarning("Champion analytics ingestion skipped: no active patch found.");
            return;
        }

        var currentPatch = currentPatchInfo.Version;
        var patchStartEpoch = new DateTimeOffset(currentPatchInfo.ReleaseDate, TimeSpan.Zero).ToUnixTimeSeconds();

        var minMatchesForPatch = Math.Max(1, jobOptions.MinimumSuccessfulMatchesForCurrentPatch);
        var staleAfterMinutes = Math.Max(5, jobOptions.DataStaleAfterMinutes);
        var staleCutoffUtc = DateTime.UtcNow.AddMinutes(-staleAfterMinutes);

        var successfulMatchesForPatch = await db.Matches
            .AsNoTracking()
            .Where(m => m.Status == FetchStatus.Success && m.Patch == currentPatch)
            .CountAsync(ct);

        var latestFetchAtUtc = await db.Matches
            .AsNoTracking()
            .Where(m => m.Status == FetchStatus.Success && m.Patch == currentPatch && m.FetchedAt != null)
            .MaxAsync(m => m.FetchedAt, ct);

        var isStale = !latestFetchAtUtc.HasValue || latestFetchAtUtc.Value <= staleCutoffUtc;
        if (successfulMatchesForPatch >= minMatchesForPatch && !isStale)
        {
            logger.LogInformation(
                "Champion analytics ingestion skipped: current patch {Patch} has {Count} successful matches and is fresh (latest {LatestFetchAtUtc}).",
                currentPatch,
                successfulMatchesForPatch,
                latestFetchAtUtc);
            return;
        }

        var maxCandidates = Math.Max(1, jobOptions.MaxCandidateSummonersPerRun);
        var maxQueued = Math.Max(1, jobOptions.MaxRefreshJobsToQueuePerRun);
        var lockTtl = TimeSpan.FromMinutes(Math.Max(2, jobOptions.RefreshLockMinutes));

        var candidates = await GetCandidatesAsync(maxCandidates, jobOptions, ct);
        if (candidates.Count == 0)
        {
            logger.LogWarning(
                "Champion analytics ingestion skipped: no candidate summoners with Riot IDs are available.");
            return;
        }

        var queued = 0;
        foreach (var candidate in candidates)
        {
            if (queued >= maxQueued) break;

            if (!PlatformRouteParser.TryParse(candidate.PlatformRegion, out var platform))
            {
                logger.LogWarning(
                    "Skipping analytics ingestion candidate due to invalid platform region {PlatformRegion} ({GameName}#{TagLine})",
                    candidate.PlatformRegion,
                    candidate.GameName,
                    candidate.TagLine);
                continue;
            }

            var lockKey = BuildRefreshKey(platform, candidate.GameName, candidate.TagLine);
            var acquired = await refreshLockRepository.TryAcquireAsync(lockKey, lockTtl, ct);
            if (!acquired) continue;

            try
            {
                backgroundJobClient.Enqueue<ISummonerRefreshJob>(job =>
                    job.RefreshForAnalytics(candidate.GameName, candidate.TagLine, platform, lockKey,
                        patchStartEpoch, currentPatch, CancellationToken.None));
                queued++;
            }
            catch (Exception)
            {
                await refreshLockRepository.ReleaseAsync(lockKey, ct);
                throw;
            }
        }

        logger.LogInformation(
            "Champion analytics ingestion queued {QueuedCount} summoner refresh jobs (patch {Patch}, matches {MatchCount}, stale {IsStale}).",
            queued,
            currentPatch,
            successfulMatchesForPatch,
            isStale);
    }

    private async Task<List<CandidateSummoner>> GetCandidatesAsync(
        int maxCandidates,
        ChampionAnalyticsIngestionJobOptions jobOptions,
        CancellationToken ct)
    {
        var combined = new List<CandidateSummoner>();

        if (jobOptions.PrioritizeFavoriteSummoners)
        {
            var favoriteCandidates = await (
                from s in db.Summoners.AsNoTracking()
                join f in db.UserFavoriteSummoners.AsNoTracking()
                    on new { Puuid = s.Puuid!, PlatformRegion = s.PlatformRegion! }
                    equals new { Puuid = f.SummonerPuuid, PlatformRegion = f.PlatformRegion }
                where s.GameName != null
                      && s.TagLine != null
                      && s.PlatformRegion != null
                select new CandidateSummoner(
                    s.PlatformRegion!,
                    s.GameName!,
                    s.TagLine!,
                    s.UpdatedAt)
            ).ToListAsync(ct);

            combined.AddRange(favoriteCandidates);
        }

        if (jobOptions.FallbackToTrackedSummoners)
        {
            var fallbackCandidates = await db.Summoners
                .AsNoTracking()
                .Where(s => s.GameName != null && s.TagLine != null && s.PlatformRegion != null)
                .OrderBy(s => s.UpdatedAt)
                .Take(maxCandidates * 3)
                .Select(s => new CandidateSummoner(
                    s.PlatformRegion!,
                    s.GameName!,
                    s.TagLine!,
                    s.UpdatedAt))
                .ToListAsync(ct);

            combined.AddRange(fallbackCandidates);
        }

        return combined
            .OrderBy(c => c.UpdatedAt)
            .DistinctBy(c =>
                $"{c.PlatformRegion.ToUpperInvariant()}:{c.GameName.ToUpperInvariant()}:{c.TagLine.ToUpperInvariant()}")
            .Take(maxCandidates)
            .ToList();
    }

    private static string BuildRefreshKey(PlatformRoute platform, string gameName, string tagLine)
    {
        return
            $"summoner-refresh:{platform}:{gameName.Trim().ToUpperInvariant()}:{tagLine.Trim().ToUpperInvariant()}";
    }

}
