using System.Diagnostics;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Caching.Hybrid;
using Transcendence.Data;
using Transcendence.Data.Models.LoL.Analytics;
using Transcendence.Data.Models.LoL.Match;
using Transcendence.Service.Core.Queries;
using Transcendence.Service.Core.Services.Analytics.Interfaces;
using Transcendence.Service.Core.Services.Jobs.Configuration;
using Transcendence.Service.Core.Services.RiotApi;

namespace Transcendence.Service.Core.Services.Jobs;

/// <summary>
/// Bounded recurring job that keeps every popular champion's DEFAULT profile-page analytics warm and fresh.
/// For each champion with enough games on the active patch it recomputes win rates / builds /
/// matchups, synergies (and optionally pro-builds) for the page-default params and OVERWRITES the cache via
/// <see cref="IChampionAnalyticsService.RefreshDefaultProfileCacheAsync"/> (gap-free SetAsync).
/// Runs on the reserved <see cref="HangfireQueues.AnalyticsWarm"/> lane (its own dedicated worker
/// pool) so it is reserved independently from ingestion and heavy batch work.
/// Per-champion work is still bounded internally to yield the DB to ingestion/API demand.
/// </summary>
[DisableConcurrentExecution(timeoutInSeconds: 60 * 60)]
public class WarmDefaultChampionProfilesJob(
    IServiceScopeFactory scopeFactory,
    TranscendenceContext db,
    IOptions<WarmDefaultChampionProfilesJobOptions> options,
    ILogger<WarmDefaultChampionProfilesJob> logger,
    HybridCache cache)
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
        // Short runs must not repeat the corpus scan twelve times per hour. Preserve the original
        // hourly eligibility cadence; cache factories own their context even if a caller cancels.
        var champions = await cache.GetOrCreateAsync(
            $"analytics:profile-warm-candidates:v1:{patch}:{minGames}",
            async cancel =>
            {
                using var queryScope = scopeFactory.CreateScope();
                var queryDb = queryScope.ServiceProvider.GetRequiredService<TranscendenceContext>();
                queryDb.Database.SetCommandTimeout(30);
                return await queryDb.MatchParticipants.AsNoTracking().OnPatch(patch).FromSuccessfulMatches()
                    .Where(mp => mp.TeamPosition != null)
                    .GroupBy(mp => mp.ChampionId)
                    .Select(group => new { ChampionId = group.Key, Games = group.Count() })
                    .Where(row => row.Games >= minGames).OrderByDescending(row => row.Games)
                    .Select(row => row.ChampionId).ToListAsync(cancel);
            },
            new HybridCacheEntryOptions { Expiration = TimeSpan.FromHours(1), LocalCacheExpiration = TimeSpan.FromHours(1) },
            cancellationToken: ct);

        if (champions.Count == 0)
        {
            logger.LogInformation(
                "Default champion profile warm: no champions with >= {MinGames} games on patch {Patch}.",
                minGames, patch);
            return;
        }

        // Persist successful coverage so short runs rotate across every champion, including after
        // a worker restart. A long champion sweep must not occupy the warm lane for hours.
        var updated = await db.AnalyticsResponseSnapshots.AsNoTracking()
            .Where(row => row.Feature == "profile-warm" && row.Patch == patch)
            .ToDictionaryAsync(row => row.ScopeKey, row => row.ComputedAtUtc, ct);
        champions = champions.OrderBy(champion => updated.GetValueOrDefault(
                $"{opts.RankTier}:{champion}", DateTime.MinValue))
            .Take(Math.Clamp(opts.MaxChampionsPerRun, 1, 200)).ToList();
        var maxConcurrency = Math.Clamp(opts.MaxConcurrency, 1, 8);
        using var gate = new SemaphoreSlim(maxConcurrency);
        var warmed = 0;
        var failed = 0;

        var tasks = champions.Select(async championId =>
        {
            await gate.WaitAsync(ct);
            try
            {
                if (stopwatch.Elapsed.TotalSeconds >= Math.Max(1, opts.MaxRunSeconds))
                    return;
                // Fresh DI scope per champion -> isolated DbContext. The analytics compute path is
                // not safe to run concurrently on a single shared context, so we don't reuse this
                // job's injected db for the per-champion work.
                using var scope = scopeFactory.CreateScope();
                var analytics = scope.ServiceProvider.GetRequiredService<IChampionAnalyticsService>();
                var effectiveRole = await analytics.RefreshDefaultProfileCacheAsync(
                    championId, opts.RankTier, opts.IncludeProBuilds, ct);
                if (effectiveRole != null)
                {
                    // A missing snapshot can still require the raw fallback during catch-up.
                    // Keep that fill bounded on this champion's isolated context.
                    scope.ServiceProvider.GetRequiredService<TranscendenceContext>().Database
                        .SetCommandTimeout(Math.Clamp(opts.SynergyCommandTimeoutSeconds, 30, 600));
                    var synergies = scope.ServiceProvider.GetRequiredService<IChampionSynergyService>();
                    await synergies.RefreshSnapshotAsync(championId, effectiveRole, opts.RankTier, patch, ct);
                }
                var context = scope.ServiceProvider.GetRequiredService<TranscendenceContext>();
                var key = $"{opts.RankTier}:{championId}";
                var coverage = await context.AnalyticsResponseSnapshots.FirstOrDefaultAsync(row =>
                    row.Feature == "profile-warm" && row.ScopeKey == key && row.Patch == patch, ct);
                if (coverage == null)
                {
                    coverage = new AnalyticsResponseSnapshot
                    {
                        Id = Guid.NewGuid(), Feature = "profile-warm", ScopeKey = key, Patch = patch, Payload = "true"
                    };
                    context.AnalyticsResponseSnapshots.Add(coverage);
                }
                coverage.ComputedAtUtc = DateTime.UtcNow;
                await context.SaveChangesAsync(ct);
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
