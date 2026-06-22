using System.Diagnostics;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Transcendence.Data;
using Transcendence.Data.Models.LoL.Match;
using Transcendence.Service.Core.Queries;
using Transcendence.Service.Core.Services.Analytics.Interfaces;
using Transcendence.Service.Core.Services.Jobs.Configuration;

namespace Transcendence.Service.Core.Services.Jobs;

/// <summary>
/// Hourly job that keeps every popular champion's DEFAULT profile-page analytics warm and fresh.
/// For each champion with enough games on the active patch it recomputes win rates / builds /
/// matchups (and optionally pro-builds) for the page-default params and OVERWRITES the cache via
/// <see cref="IChampionAnalyticsService.RefreshDefaultProfileCacheAsync"/> (gap-free SetAsync).
/// Runs on the reserved <see cref="HangfireQueues.AnalyticsWarm"/> lane (its own dedicated worker
/// pool) so it is always ready regardless of how saturated the shared refresh queues are.
/// Per-champion work is still bounded internally to yield the DB to ingestion/API demand.
/// </summary>
[DisableConcurrentExecution(timeoutInSeconds: 60 * 60)]
public class WarmDefaultChampionProfilesJob(
    IServiceScopeFactory scopeFactory,
    TranscendenceContext db,
    IOptions<WarmDefaultChampionProfilesJobOptions> options,
    ILogger<WarmDefaultChampionProfilesJob> logger)
{
    [Queue(HangfireQueues.AnalyticsWarm)]
    public async Task ExecuteAsync(CancellationToken ct)
    {
        var opts = options.Value;
        var stopwatch = Stopwatch.StartNew();

        var patch = await db.Patches
            .AsNoTracking()
            .Where(p => p.IsActive)
            .Select(p => p.Version)
            .FirstOrDefaultAsync(ct);

        if (string.IsNullOrWhiteSpace(patch))
        {
            logger.LogWarning("Default champion profile warm skipped: no active patch found.");
            return;
        }

        var minGames = Math.Max(1, opts.MinimumGamesToWarm);
        var champions = await db.MatchParticipants
            .AsNoTracking()
            .OnPatch(patch)
            .FromSuccessfulMatches()
            .Where(mp => mp.TeamPosition != null)
            .GroupBy(mp => mp.ChampionId)
            .Select(g => new { ChampionId = g.Key, Games = g.Count() })
            .Where(x => x.Games >= minGames)
            .OrderByDescending(x => x.Games)
            .Select(x => x.ChampionId)
            .ToListAsync(ct);

        if (champions.Count == 0)
        {
            logger.LogInformation(
                "Default champion profile warm: no champions with >= {MinGames} games on patch {Patch}.",
                minGames, patch);
            return;
        }

        var maxConcurrency = Math.Clamp(opts.MaxConcurrency, 1, 8);
        using var gate = new SemaphoreSlim(maxConcurrency);
        var warmed = 0;
        var failed = 0;

        var tasks = champions.Select(async championId =>
        {
            await gate.WaitAsync(ct);
            try
            {
                // Fresh DI scope per champion -> isolated DbContext. The analytics compute path is
                // not safe to run concurrently on a single shared context, so we don't reuse this
                // job's injected db for the per-champion work.
                using var scope = scopeFactory.CreateScope();
                var analytics = scope.ServiceProvider.GetRequiredService<IChampionAnalyticsService>();
                await analytics.RefreshDefaultProfileCacheAsync(
                    championId, opts.RankTier, opts.IncludeProBuilds, ct);
                Interlocked.Increment(ref warmed);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref failed);
                logger.LogWarning(ex, "Failed to warm default profile for champion {ChampionId}", championId);
            }
            finally
            {
                gate.Release();
            }
        });

        await Task.WhenAll(tasks);

        stopwatch.Stop();
        logger.LogInformation(
            "Default champion profile warm complete: {Warmed}/{Total} warmed ({Failed} failed) at tier {Tier} " +
            "(pro-builds={Pro}) for patch {Patch} in {Elapsed}ms",
            warmed, champions.Count, failed, opts.RankTier, opts.IncludeProBuilds, patch, stopwatch.ElapsedMilliseconds);
    }
}
