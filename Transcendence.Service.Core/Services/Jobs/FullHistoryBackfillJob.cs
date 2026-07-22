using Camille.Enums;
using Camille.RiotGames;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Options;
using Transcendence.Data;
using Transcendence.Data.Models.LoL.Account;
using Transcendence.Data.Repositories.Interfaces;
using Transcendence.Service.Core.Services.Analysis;
using Transcendence.Service.Core.Services.Jobs.Configuration;
using Transcendence.Service.Core.Services.Jobs.Interfaces;
using Transcendence.Service.Core.Services.RiotApi;

namespace Transcendence.Service.Core.Services.Jobs;

public sealed class FullHistoryBackfillJob(
    TranscendenceContext db,
    LeagueRiotApiContext riotApiContext,
    IRiotRateGate rateGate,
    IRiotMatchIdsClient riotMatchIdsClient,
    IBackgroundJobClient backgroundJobClient,
    HybridCache cache,
    IOptions<FullHistoryBackfillJobOptions> options,
    IRefreshLockRepository refreshLockRepository,
    ILogger<FullHistoryBackfillJob> logger)
{
    private const string RankedSoloQueueType = "RANKED_SOLO_5x5";
    private const string RankedSoloQueueScope = QueueCatalog.QueueFamilyRankedSoloDuo;
    private const int AggregationVersion = 1;

    private sealed record FactBuildResult(SummonerMatchFact? Fact, long? MatchEpochSeconds, string? FailureMessage);

    [Queue(HangfireQueues.HistoryBackfill)]
    public async Task ProcessAsync(
        Guid summonerId,
        Guid? requestedByUserAccountId = null,
        CancellationToken ct = default)
    {
        var jobOptions = options.Value;
        if (!jobOptions.Enabled)
        {
            logger.LogInformation("[FullHistory] Backfill disabled; skipping summoner {SummonerId}.", summonerId);
            return;
        }

        // Serialize per summoner. Overlapping runs (a fresh enqueue from the refresh path colliding
        // with an in-flight self-continuation chain) double-fetch the same matches — wasting the
        // scarce personal-tier Riot budget — and collide on the SummonerMatchFacts unique index,
        // rolling back the whole page. A short lease admits one run at a time; overlapping
        // invocations skip. The lease TTL bounds recovery if a run dies without releasing.
        var backfillLockKey = $"fullhistory-backfill:{summonerId:N}";
        if (!await refreshLockRepository.TryAcquireAsync(backfillLockKey, TimeSpan.FromMinutes(15), ct))
        {
            logger.LogInformation(
                "[FullHistory] Backfill already running for summoner {SummonerId}; skipping overlapping run.",
                summonerId);
            return;
        }

        try
        {
            await RunBackfillAsync(summonerId, requestedByUserAccountId, jobOptions, ct);
        }
        finally
        {
            await refreshLockRepository.ReleaseAsync(backfillLockKey, ct);
        }
    }

    private async Task RunBackfillAsync(
        Guid summonerId,
        Guid? requestedByUserAccountId,
        FullHistoryBackfillJobOptions jobOptions,
        CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var pageSize = Math.Clamp(jobOptions.PageSize, 1, 100);
        var maxPages = Math.Max(1, jobOptions.MaxPagesPerRun);
        var lowerBoundEpochSeconds = Math.Max(0, jobOptions.MinimumMatchStartEpochSeconds);
        var touchedSeasons = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var summoner = await db.Summoners
            .FirstOrDefaultAsync(s => s.Id == summonerId, ct);
        if (summoner == null || string.IsNullOrWhiteSpace(summoner.Puuid))
        {
            logger.LogWarning("[FullHistory] Summoner {SummonerId} is missing or has no PUUID; skipping.", summonerId);
            return;
        }

        if (!PlatformRouteParser.TryParse(summoner.PlatformRegion ?? string.Empty, out var platformRoute))
        {
            logger.LogWarning(
                "[FullHistory] Summoner {SummonerId} has unsupported platform region {PlatformRegion}; skipping.",
                summonerId,
                summoner.PlatformRegion);
            return;
        }

        var regionalRoute = platformRoute.ToRegional();
        var configuredSeasons = await RankedSeasonResolver.GetConfiguredSeasonsAsync(db, ct);
        var activeSeason = await RankedSeasonResolver.GetActiveSeasonAsync(db, now, ct);

        var backfill = await GetOrStartBackfillAsync(summonerId, requestedByUserAccountId, now, ct);
        if (IsTerminalStatus(backfill.Status) && requestedByUserAccountId == null)
        {
            await RecomputeSeasonAggregatesAsync(summonerId, activeSeason.SeasonKey, backfill.Status, now, ct);
            return;
        }

        if (backfill.CursorEndEpochSeconds.HasValue &&
            backfill.CursorEndEpochSeconds.Value <= lowerBoundEpochSeconds)
        {
            await CompleteBackfillAsync(backfill, summonerId, activeSeason.SeasonKey, touchedSeasons, now, ct);
            return;
        }

        await RetryOutstandingFailuresAsync(
            summoner,
            regionalRoute,
            configuredSeasons,
            touchedSeasons,
            jobOptions,
            now,
            ct);

        var shouldContinue = false;
        for (var page = 0; page < maxPages; page++)
        {
            ct.ThrowIfCancellationRequested();

            var matchIdPage = await riotMatchIdsClient.GetMatchIdsByPuuidAsync(
                    regionalRoute,
                    summoner.Puuid!,
                    pageSize,
                    backfill.CursorEndEpochSeconds,
                    queue: null,
                    startTimeEpochSeconds: lowerBoundEpochSeconds,
                    start: 0,
                    type: null,
                    ct);
            if (matchIdPage == null)
            {
                // This is rate-gate backpressure, not end-of-history. Keep the row Running and enqueue
                // the normal continuation so a momentary budget miss can never mark the backfill done.
                backfill.Status = SummonerFullHistoryBackfillStatuses.Running;
                backfill.UpdatedAtUtc = now;
                await db.SaveChangesAsync(ct);
                shouldContinue = true;
                logger.LogInformation(
                    "[FullHistory] Match-id page deferred for summoner {SummonerId}; preserving cursor {Cursor} and retrying later.",
                    summonerId,
                    backfill.CursorEndEpochSeconds);
                break;
            }

            var pageIds = matchIdPage
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            if (pageIds.Count == 0)
            {
                await CompleteBackfillAsync(backfill, summonerId, activeSeason.SeasonKey, touchedSeasons, now, ct);
                return;
            }

            backfill.PagesScanned++;
            backfill.MatchIdsDiscovered += pageIds.Count;

            var existingFacts = await db.SummonerMatchFacts
                .AsNoTracking()
                .Where(f => f.SummonerId == summonerId && pageIds.Contains(f.MatchId))
                .Select(f => new { f.MatchId, f.MatchDate, f.SeasonKey })
                .ToListAsync(ct);

            var existingById = existingFacts.ToDictionary(x => x.MatchId, StringComparer.Ordinal);
            var oldestSeenEpochSeconds = existingFacts
                .Where(f => f.MatchDate > 0)
                .Select(f => f.MatchDate / 1000)
                .DefaultIfEmpty(long.MaxValue)
                .Min();

            foreach (var seasonKey in existingFacts.Select(x => x.SeasonKey).Where(x => !string.IsNullOrWhiteSpace(x)))
                touchedSeasons.Add(seasonKey);

            var pendingIds = pageIds
                .Where(id => !existingById.ContainsKey(id))
                .ToList();

            backfill.SkippedExistingFacts += pageIds.Count - pendingIds.Count;

            foreach (var matchId in pendingIds)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var result = await BuildFactAsync(
                        summoner,
                        matchId,
                        regionalRoute,
                        platformRoute,
                        configuredSeasons,
                        now,
                        ct);

                    if (result.MatchEpochSeconds.HasValue && result.MatchEpochSeconds.Value < oldestSeenEpochSeconds)
                        oldestSeenEpochSeconds = result.MatchEpochSeconds.Value;

                    if (result.Fact == null)
                    {
                        backfill.DetailFetchFailures++;
                        await RecordFetchFailureAsync(
                            summonerId,
                            matchId,
                            platformRoute.ToString(),
                            regionalRoute.ToString(),
                            result.FailureMessage ?? "Match detail unavailable.",
                            now,
                            ct);
                        continue;
                    }

                    db.SummonerMatchFacts.Add(result.Fact);
                    touchedSeasons.Add(result.Fact.SeasonKey);
                    backfill.FactsPersisted++;
                    await MarkFetchFailureResolvedAsync(summonerId, matchId, now, ct);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // Isolate per-match failures. A single transient Riot error (5xx/timeout/parse)
                    // must not escape ProcessAsync — that killed the self-continuation chain and left
                    // the backfill stranded in 'Running' with no resumer. Record it like an unavailable
                    // detail and continue; the outstanding-failure retry sweep re-attempts it later.
                    backfill.DetailFetchFailures++;
                    var message = ex.Message.Length > 512 ? ex.Message[..512] : ex.Message;
                    await RecordFetchFailureAsync(
                        summonerId,
                        matchId,
                        platformRoute.ToString(),
                        regionalRoute.ToString(),
                        message,
                        now,
                        ct);
                    logger.LogWarning(
                        ex,
                        "[FullHistory] Match {MatchId} for summoner {SummonerId} threw; recorded as a failure and continuing.",
                        matchId,
                        summonerId);
                }
            }

            if (oldestSeenEpochSeconds != long.MaxValue)
            {
                var nextCursor = Math.Max(0, oldestSeenEpochSeconds - 1);
                backfill.CursorEndEpochSeconds = !backfill.CursorEndEpochSeconds.HasValue ||
                                                nextCursor < backfill.CursorEndEpochSeconds.Value
                    ? nextCursor
                    : backfill.CursorEndEpochSeconds;
            }

            backfill.Status = SummonerFullHistoryBackfillStatuses.Running;
            backfill.UpdatedAtUtc = now;
            await db.SaveChangesAsync(ct);

            if (pageIds.Count < pageSize ||
                (backfill.CursorEndEpochSeconds.HasValue &&
                 backfill.CursorEndEpochSeconds.Value <= lowerBoundEpochSeconds))
            {
                await CompleteBackfillAsync(backfill, summonerId, activeSeason.SeasonKey, touchedSeasons, now, ct);
                return;
            }

            shouldContinue = true;
        }

        foreach (var seasonKey in touchedSeasons.Append(activeSeason.SeasonKey).Distinct(StringComparer.OrdinalIgnoreCase))
            await RecomputeSeasonAggregatesAsync(summonerId, seasonKey, backfill.Status, now, ct);

        await cache.RemoveByTagAsync($"summoner-stats:{summonerId}", ct);

        if (shouldContinue)
        {
            backgroundJobClient.Enqueue<FullHistoryBackfillJob>(job =>
                job.ProcessAsync(summonerId, null, CancellationToken.None));
        }
    }

    private async Task<SummonerFullHistoryBackfill> GetOrStartBackfillAsync(
        Guid summonerId,
        Guid? requestedByUserAccountId,
        DateTime now,
        CancellationToken ct)
    {
        var backfill = await db.SummonerFullHistoryBackfills
            .FirstOrDefaultAsync(x => x.SummonerId == summonerId && x.Scope == SummonerFullHistoryScopes.FullHistory, ct);

        if (backfill == null)
        {
            backfill = new SummonerFullHistoryBackfill
            {
                Id = Guid.NewGuid(),
                SummonerId = summonerId,
                Scope = SummonerFullHistoryScopes.FullHistory,
                Status = SummonerFullHistoryBackfillStatuses.Queued,
                RequestedByUserAccountId = requestedByUserAccountId,
                RequestedAtUtc = now,
                StartedAtUtc = now,
                UpdatedAtUtc = now
            };
            db.SummonerFullHistoryBackfills.Add(backfill);
        }
        else if (IsTerminalStatus(backfill.Status) &&
                 requestedByUserAccountId == null)
        {
            return backfill;
        }
        else if (requestedByUserAccountId.HasValue)
        {
            backfill.RequestedByUserAccountId = requestedByUserAccountId;
            backfill.RequestedAtUtc = now;
            backfill.Status = SummonerFullHistoryBackfillStatuses.Queued;
            backfill.CursorEndEpochSeconds = null;
            backfill.CompletedAtUtc = null;
            backfill.LastErrorMessage = null;
            backfill.UpdatedAtUtc = now;
            backfill.StartedAtUtc ??= now;
        }

        backfill.Status = SummonerFullHistoryBackfillStatuses.Running;
        backfill.StartedAtUtc ??= now;
        backfill.UpdatedAtUtc = now;
        await db.SaveChangesAsync(ct);
        return backfill;
    }

    private async Task<FactBuildResult> BuildFactAsync(
        Summoner summoner,
        string matchId,
        RegionalRoute regionalRoute,
        PlatformRoute platformRoute,
        IReadOnlyList<RankedSeasonWindow> configuredSeasons,
        DateTime now,
        CancellationToken ct)
    {
        if (!await rateGate.AcquireAsync(regionalRoute.ToString(), ct))
            return new FactBuildResult(null, null, "Riot rate gate unavailable for match detail fetch.");

        var matchDto = await riotApiContext.Api.MatchV5().GetMatchAsync(regionalRoute, matchId, ct);
        if (matchDto == null)
            return new FactBuildResult(null, null, "Riot API returned null match detail.");

        var info = matchDto.Info;
        var participant = info.Participants
            .FirstOrDefault(p => string.Equals(p.Puuid, summoner.Puuid, StringComparison.Ordinal));

        var matchEpochSeconds = info.GameCreation > 0 ? info.GameCreation / 1000 : (long?)null;
        if (participant == null)
            return new FactBuildResult(null, matchEpochSeconds, "Target summoner was not present in match detail.");

        var queueId = (int)info.QueueId;
        var matchUtc = DateTimeOffset.FromUnixTimeMilliseconds(info.GameCreation).UtcDateTime;
        var seasonKey = RankedSeasonResolver.ResolveSeasonKey(matchUtc, configuredSeasons);
        var classification = RankedMatchCountClassifier.Classify(
            queueId,
            info.EndOfGameResult,
            participant.GameEndedInEarlySurrender);

        var fact = new SummonerMatchFact
        {
            Id = Guid.NewGuid(),
            SummonerId = summoner.Id,
            MatchId = matchDto.Metadata.MatchId,
            Puuid = summoner.Puuid!,
            PlatformRegion = platformRoute.ToString(),
            RegionalRoute = regionalRoute.ToString(),
            MatchDate = info.GameCreation,
            SeasonKey = seasonKey,
            Patch = NormalizePatch(info.GameVersion),
            QueueId = queueId,
            QueueType = QueueCatalog.ResolveQueueLabel(queueId),
            QueueFamily = QueueCatalog.ResolveQueueFamily(queueId),
            DurationSeconds = (int)info.GameDuration,
            EndOfGameResult = info.EndOfGameResult,
            ParticipantId = participant.ParticipantId,
            TeamId = (int)participant.TeamId,
            ChampionId = (int)participant.ChampionId,
            TeamPosition = !string.IsNullOrWhiteSpace(participant.TeamPosition)
                ? participant.TeamPosition
                : participant.IndividualPosition,
            IndividualPosition = participant.IndividualPosition,
            Win = participant.Win,
            Kills = participant.Kills,
            Deaths = participant.Deaths,
            Assists = participant.Assists,
            VisionScore = participant.VisionScore,
            TotalDamageDealtToChampions = participant.TotalDamageDealtToChampions,
            TotalMinionsKilled = participant.TotalMinionsKilled,
            NeutralMinionsKilled = participant.NeutralMinionsKilled,
            SummonerSpell1Id = participant.Summoner1Id,
            SummonerSpell2Id = participant.Summoner2Id,
            GameEndedInEarlySurrender = participant.GameEndedInEarlySurrender,
            GameEndedInSurrender = participant.GameEndedInSurrender,
            TeamEarlySurrendered = participant.TeamEarlySurrendered,
            CountsTowardRankedTotal = classification.CountsTowardRankedTotal,
            RankedCountClassifierVersion = RankedMatchCountClassifier.Version,
            RankedCountExclusionReason = classification.ExclusionReason,
            FetchedAtUtc = now,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        return new FactBuildResult(fact, matchEpochSeconds, null);
    }

    private async Task RetryOutstandingFailuresAsync(
        Summoner summoner,
        RegionalRoute regionalRoute,
        IReadOnlyList<RankedSeasonWindow> configuredSeasons,
        ISet<string> touchedSeasons,
        FullHistoryBackfillJobOptions jobOptions,
        DateTime now,
        CancellationToken ct)
    {
        var retryLimit = Math.Max(0, jobOptions.MaxFailureRetriesPerRun);
        if (retryLimit == 0)
            return;

        var failures = await db.SummonerMatchFactFetchFailures
            .Where(x => x.SummonerId == summoner.Id && x.ResolvedAtUtc == null)
            .OrderBy(x => x.LastAttemptAtUtc)
            .Take(retryLimit)
            .ToListAsync(ct);

        if (failures.Count == 0)
            return;

        if (!PlatformRouteParser.TryParse(summoner.PlatformRegion ?? string.Empty, out var platformRoute))
            return;

        foreach (var failure in failures)
        {
            ct.ThrowIfCancellationRequested();
            var existing = await db.SummonerMatchFacts
                .AnyAsync(x => x.SummonerId == summoner.Id && x.MatchId == failure.MatchId, ct);
            if (existing)
            {
                failure.ResolvedAtUtc = now;
                continue;
            }

            var result = await BuildFactAsync(
                summoner,
                failure.MatchId,
                regionalRoute,
                platformRoute,
                configuredSeasons,
                now,
                ct);

            failure.AttemptCount++;
            failure.LastAttemptAtUtc = now;

            if (result.Fact == null)
            {
                failure.LastErrorMessage = result.FailureMessage ?? "Match detail unavailable.";
                continue;
            }

            db.SummonerMatchFacts.Add(result.Fact);
            touchedSeasons.Add(result.Fact.SeasonKey);
            failure.ResolvedAtUtc = now;
        }

        await db.SaveChangesAsync(ct);
    }

    private async Task CompleteBackfillAsync(
        SummonerFullHistoryBackfill backfill,
        Guid summonerId,
        string activeSeasonKey,
        IEnumerable<string> touchedSeasonKeys,
        DateTime now,
        CancellationToken ct)
    {
        var unresolvedFailures = await db.SummonerMatchFactFetchFailures
            .CountAsync(x => x.SummonerId == summonerId && x.ResolvedAtUtc == null, ct);

        backfill.Status = unresolvedFailures > 0
            ? SummonerFullHistoryBackfillStatuses.CompletedWithGaps
            : SummonerFullHistoryBackfillStatuses.Completed;
        backfill.CompletedAtUtc = now;
        backfill.UpdatedAtUtc = now;
        await db.SaveChangesAsync(ct);

        foreach (var seasonKey in touchedSeasonKeys.Append(activeSeasonKey).Distinct(StringComparer.OrdinalIgnoreCase))
            await RecomputeSeasonAggregatesAsync(summonerId, seasonKey, backfill.Status, now, ct);

        await cache.RemoveByTagAsync($"summoner-stats:{summonerId}", ct);
    }

    private async Task RecordFetchFailureAsync(
        Guid summonerId,
        string matchId,
        string platformRegion,
        string regionalRoute,
        string errorMessage,
        DateTime now,
        CancellationToken ct)
    {
        var failure = await db.SummonerMatchFactFetchFailures
            .FirstOrDefaultAsync(x => x.SummonerId == summonerId && x.MatchId == matchId, ct);

        if (failure == null)
        {
            db.SummonerMatchFactFetchFailures.Add(new SummonerMatchFactFetchFailure
            {
                Id = Guid.NewGuid(),
                SummonerId = summonerId,
                MatchId = matchId,
                PlatformRegion = platformRegion,
                RegionalRoute = regionalRoute,
                AttemptCount = 1,
                LastErrorMessage = Truncate(errorMessage, 1024),
                FirstAttemptAtUtc = now,
                LastAttemptAtUtc = now
            });
            return;
        }

        failure.AttemptCount++;
        failure.PlatformRegion = platformRegion;
        failure.RegionalRoute = regionalRoute;
        failure.LastErrorMessage = Truncate(errorMessage, 1024);
        failure.LastAttemptAtUtc = now;
        failure.ResolvedAtUtc = null;
    }

    private async Task MarkFetchFailureResolvedAsync(Guid summonerId, string matchId, DateTime now, CancellationToken ct)
    {
        var failure = await db.SummonerMatchFactFetchFailures
            .FirstOrDefaultAsync(x => x.SummonerId == summonerId && x.MatchId == matchId, ct);
        if (failure != null)
            failure.ResolvedAtUtc = now;
    }

    private async Task RecomputeSeasonAggregatesAsync(
        Guid summonerId,
        string seasonKey,
        string backfillStatus,
        DateTime now,
        CancellationToken ct)
    {
        var facts = await db.SummonerMatchFacts
            .AsNoTracking()
            .Where(f =>
                f.SummonerId == summonerId &&
                f.SeasonKey == seasonKey &&
                f.QueueId == QueueCatalog.RankedSoloDuoQueueId &&
                f.CountsTowardRankedTotal)
            .Select(f => new
            {
                f.ChampionId,
                f.Win,
                f.Kills,
                f.Deaths,
                f.Assists,
                f.VisionScore,
                f.TotalDamageDealtToChampions,
                Cs = f.TotalMinionsKilled + f.NeutralMinionsKilled,
                f.DurationSeconds
            })
            .ToListAsync(ct);

        // Delete-then-reinsert must be atomic: without a transaction, uncached profile reads between
        // the ExecuteDelete and the SaveChanges observe empty season stats, and a crash mid-recompute
        // leaves the season's aggregates missing until the next backfill.
        await using var recomputeTx = await db.Database.BeginTransactionAsync(ct);

        await db.SummonerSeasonOverviewStats
            .Where(x => x.SummonerId == summonerId && x.SeasonKey == seasonKey && x.QueueScope == RankedSoloQueueScope)
            .ExecuteDeleteAsync(ct);
        await db.SummonerSeasonChampionStats
            .Where(x => x.SummonerId == summonerId && x.SeasonKey == seasonKey && x.QueueScope == RankedSoloQueueScope)
            .ExecuteDeleteAsync(ct);

        if (facts.Count > 0)
        {
            db.SummonerSeasonOverviewStats.Add(new SummonerSeasonOverviewStat
            {
                Id = Guid.NewGuid(),
                SummonerId = summonerId,
                SeasonKey = seasonKey,
                QueueScope = RankedSoloQueueScope,
                TotalMatches = facts.Count,
                Wins = facts.Sum(x => x.Win ? 1 : 0),
                Losses = facts.Sum(x => x.Win ? 0 : 1),
                TotalKills = facts.Sum(x => (long)x.Kills),
                TotalDeaths = facts.Sum(x => (long)x.Deaths),
                TotalAssists = facts.Sum(x => (long)x.Assists),
                TotalVisionScore = facts.Sum(x => (long)x.VisionScore),
                TotalDamageToChamps = facts.Sum(x => (long)x.TotalDamageDealtToChampions),
                TotalCs = facts.Sum(x => (long)x.Cs),
                TotalDurationSeconds = facts.Sum(x => (long)x.DurationSeconds),
                AggregationVersion = AggregationVersion,
                UpdatedAtUtc = now
            });

            foreach (var group in facts.GroupBy(x => x.ChampionId))
            {
                var games = group.Count();
                var wins = group.Sum(x => x.Win ? 1 : 0);
                db.SummonerSeasonChampionStats.Add(new SummonerSeasonChampionStat
                {
                    Id = Guid.NewGuid(),
                    SummonerId = summonerId,
                    SeasonKey = seasonKey,
                    QueueScope = RankedSoloQueueScope,
                    ChampionId = group.Key,
                    Games = games,
                    Wins = wins,
                    Losses = games - wins,
                    TotalKills = group.Sum(x => (long)x.Kills),
                    TotalDeaths = group.Sum(x => (long)x.Deaths),
                    TotalAssists = group.Sum(x => (long)x.Assists),
                    TotalVisionScore = group.Sum(x => (long)x.VisionScore),
                    TotalDamageToChamps = group.Sum(x => (long)x.TotalDamageDealtToChampions),
                    TotalCs = group.Sum(x => (long)x.Cs),
                    TotalDurationSeconds = group.Sum(x => (long)x.DurationSeconds),
                    AggregationVersion = AggregationVersion,
                    UpdatedAtUtc = now
                });
            }
        }

        var soloRank = await db.Ranks
            .AsNoTracking()
            .Where(r => r.SummonerId == summonerId && r.QueueType == RankedSoloQueueType)
            .OrderByDescending(r => r.UpdatedAt)
            .FirstOrDefaultAsync(ct);

        var riotWins = soloRank?.Wins;
        var riotLosses = soloRank?.Losses;
        var riotTotal = soloRank == null ? (int?)null : soloRank.Wins + soloRank.Losses;
        var delta = riotTotal.HasValue ? facts.Count - riotTotal.Value : (int?)null;
        var coverageStatus = riotTotal.HasValue
            ? delta == 0 ? "MATCHED_RIOT_TOTAL" : "COUNT_MISMATCH"
            : "NO_RIOT_RANK_TOTAL";

        var coverage = await db.SummonerSeasonCoverages
            .FirstOrDefaultAsync(x => x.SummonerId == summonerId &&
                                      x.SeasonKey == seasonKey &&
                                      x.QueueScope == RankedSoloQueueScope, ct);
        if (coverage == null)
        {
            coverage = new SummonerSeasonCoverage
            {
                Id = Guid.NewGuid(),
                SummonerId = summonerId,
                SeasonKey = seasonKey,
                QueueScope = RankedSoloQueueScope
            };
            db.SummonerSeasonCoverages.Add(coverage);
        }

        coverage.BackfillStatus = backfillStatus;
        coverage.CompletedMatchCount = facts.Count;
        coverage.RiotWins = riotWins;
        coverage.RiotLosses = riotLosses;
        coverage.RiotTotal = riotTotal;
        coverage.RankedCountDelta = delta;
        coverage.CoverageStatus = coverageStatus;
        coverage.ClassifierVersion = RankedMatchCountClassifier.Version;
        coverage.LastComparedAtUtc = now;
        coverage.LastBackfilledAtUtc = now;
        coverage.UpdatedAtUtc = now;

        await db.SaveChangesAsync(ct);
        await recomputeTx.CommitAsync(ct);
    }

    private static string NormalizePatch(string? gameVersion)
    {
        if (string.IsNullOrWhiteSpace(gameVersion)) return string.Empty;
        var parts = gameVersion.Split('.');
        return parts.Length >= 2 ? $"{parts[0]}.{parts[1]}" : gameVersion;
    }

    private static string Truncate(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : value[..maxLength];
    }

    private static bool IsTerminalStatus(string status)
    {
        return status is SummonerFullHistoryBackfillStatuses.Completed
            or SummonerFullHistoryBackfillStatuses.CompletedWithGaps;
    }
}
