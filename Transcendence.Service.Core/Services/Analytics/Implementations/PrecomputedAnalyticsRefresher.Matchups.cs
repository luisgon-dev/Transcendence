using System.Diagnostics;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Transcendence.Data;
using Transcendence.Data.Models.LoL.Account;
using Transcendence.Data.Models.LoL.Analytics;
using Transcendence.Data.Models.LoL.Match;
using Transcendence.Service.Core.Queries;
using Transcendence.Service.Core.Services.Analytics;
using Transcendence.Service.Core.Services.Analytics.Interfaces;
using Transcendence.Service.Core.Services.Analytics.Models;
using Transcendence.Service.Core.Services.Diagnostics;
using Transcendence.Service.Core.Services.RiotApi;

namespace Transcendence.Service.Core.Services.Analytics.Implementations;

// Matchup snapshot materialization, promotion and crash recovery form one lifecycle.
public partial class PrecomputedAnalyticsRefresher
{
    public async Task<int> RefreshMatchupsAsync(string patch, CancellationToken ct)
    {
        var generationStopwatch = Stopwatch.StartNew();
        var previousTimeout = _context.Database.GetCommandTimeout();
        _context.Database.SetCommandTimeout(Math.Clamp(_precomputedOptions.CommandTimeoutSeconds, 15, 600));
        ChampionMatchupSnapshot? snapshot = null;

        try
        {
            snapshot = await LoadResumableMatchupSnapshotAsync(patch, ct);
            if (snapshot == null)
            {
                await MaterializeMatchupFactsAsync(patch, ct);
                snapshot = await CreateMatchupSnapshotAsync(patch, ct);
            }

            snapshot.AttemptCount++;
            snapshot.LastAttemptAtUtc = DateTime.UtcNow;
            snapshot.FailureReason = null;
            await _context.SaveChangesAsync(ct);
            _telemetry?.RecordGenerationStarted(snapshot.AttemptCount);

            await EnsureRankSnapshotAsync(snapshot, ct);

            var championIds = await _context.ChampionMatchupFacts
                .AsNoTracking()
                .Where(fact => fact.Patch == patch && fact.UpdatedAtUtc <= snapshot.SourceCutoffUtc)
                .Select(fact => fact.ChampionId)
                .Distinct()
                .OrderBy(championId => championId)
                .ToListAsync(ct);
            var processedChampionIds = await _context.ChampionMatchupStats
                .AsNoTracking()
                .Where(stat => stat.SnapshotId == snapshot.Id)
                .Select(stat => stat.ChampionId)
                .Distinct()
                .ToListAsync(ct);
            var processed = processedChampionIds.ToHashSet();
            var pending = championIds.Where(championId => !processed.Contains(championId)).ToList();
            var batchSize = Math.Clamp(_precomputedOptions.MatchupChampionBatchSize, 1, 100);

            foreach (var championBatch in pending.Chunk(batchSize))
                await AggregateMatchupBatchWithSplitAsync(snapshot, championBatch, split: false, ct);

            var rowCount = await _context.ChampionMatchupStats
                .AsNoTracking()
                .CountAsync(stat => stat.SnapshotId == snapshot.Id, ct);
            await PromoteMatchupSnapshotAsync(snapshot, rowCount, ct);
            await CleanupMatchupSnapshotsAsync(patch, snapshot.Id, ct);

            _telemetry?.RecordGenerationSucceeded(rowCount, generationStopwatch.Elapsed.TotalMilliseconds);
            _logger.LogInformation(
                "Precompute refresh (matchups) patch {Patch}: promoted generation {SnapshotId} with {Rows} rows across {Champions} champions on attempt {Attempt}.",
                patch,
                snapshot.Id,
                rowCount,
                championIds.Count,
                snapshot.AttemptCount);
            return rowCount;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (snapshot != null)
            {
                snapshot.FailureReason = Truncate(exception.GetBaseException().Message, 1024);
                snapshot.LastAttemptAtUtc = DateTime.UtcNow;
                await TrySaveFailureStateAsync(snapshot, ct);
                _telemetry?.RecordGenerationFailed(snapshot.AttemptCount, generationStopwatch.Elapsed.TotalMilliseconds);
            }
            throw;
        }
        finally
        {
            _context.Database.SetCommandTimeout(previousTimeout);
        }
    }

    private async Task<ChampionMatchupSnapshot?> LoadResumableMatchupSnapshotAsync(
        string patch,
        CancellationToken ct)
    {
        var snapshot = await _context.ChampionMatchupSnapshots
            .Where(row => row.Patch == patch && row.Status == ChampionMatchupSnapshotStatus.Building)
            .OrderByDescending(row => row.StartedAtUtc)
            .FirstOrDefaultAsync(ct);
        if (snapshot == null)
            return null;

        var maxAttempts = Math.Clamp(_precomputedOptions.MaxGenerationResumeAttempts, 1, 20);
        if (snapshot.AttemptCount < maxAttempts)
        {
            _logger.LogInformation(
                "Resuming matchup generation {SnapshotId} for patch {Patch} at {Processed}/{Total} champions (attempt {Attempt}).",
                snapshot.Id,
                patch,
                snapshot.ProcessedChampionCount,
                snapshot.TotalChampionCount,
                snapshot.AttemptCount + 1);
            return snapshot;
        }

        snapshot.Status = ChampionMatchupSnapshotStatus.Failed;
        snapshot.FailureReason = Truncate(
            $"Abandoned after {snapshot.AttemptCount} failed attempts. {snapshot.FailureReason}",
            1024);
        snapshot.CompletedAtUtc = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);
        _logger.LogWarning(
            "Abandoned matchup generation {SnapshotId} for patch {Patch} after {Attempts} attempts; a fresh generation will be created.",
            snapshot.Id,
            patch,
            snapshot.AttemptCount);

        await MaterializeMatchupFactsAsync(patch, ct);
        return await CreateMatchupSnapshotAsync(patch, ct);
    }

    private async Task<ChampionMatchupSnapshot> CreateMatchupSnapshotAsync(string patch, CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow;
        var sourceFactCount = await _context.ChampionMatchupFacts
            .AsNoTracking()
            .CountAsync(fact => fact.Patch == patch && fact.UpdatedAtUtc <= cutoff, ct);
        var championCount = await _context.ChampionMatchupFacts
            .AsNoTracking()
            .Where(fact => fact.Patch == patch && fact.UpdatedAtUtc <= cutoff)
            .Select(fact => fact.ChampionId)
            .Distinct()
            .CountAsync(ct);
        var snapshot = new ChampionMatchupSnapshot
        {
            Id = Guid.NewGuid(),
            Patch = patch,
            Status = ChampionMatchupSnapshotStatus.Building,
            IsActive = false,
            StartedAtUtc = cutoff,
            SourceCutoffUtc = cutoff,
            SourceFactCount = sourceFactCount,
            TotalChampionCount = championCount
        };
        _context.ChampionMatchupSnapshots.Add(snapshot);
        await _context.SaveChangesAsync(ct);
        return snapshot;
    }

    private async Task MaterializeMatchupFactsAsync(string patch, CancellationToken ct)
    {
        var batchSize = Math.Clamp(_precomputedOptions.MatchupSourceMatchBatchSize, 10, 2_000);

        while (true)
        {
            var matchIds = await _context.Matches
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(match =>
                    match.Patch == patch &&
                    match.Status == FetchStatus.Success &&
                    (match.QueueId == QueueCatalog.RankedSoloDuoQueueId ||
                     (match.QueueId == 0 &&
                      match.QueueType == QueueCatalog.RankedSoloDuoQueueId.ToString())) &&
                    !_context.ChampionMatchupSourceMatches.Any(source => source.MatchId == match.Id))
                .OrderBy(match => match.Id)
                .Select(match => match.Id)
                .Take(batchSize)
                .ToListAsync(ct);
            if (matchIds.Count == 0)
                break;

            await MaterializeMatchupSourceBatchAsync(patch, matchIds, ct);
        }

        // Timeline backfill can arrive after participant facts. Rebuild only source matches whose
        // minute-15 timeline rows advanced since their ledger entry was written.
        while (true)
        {
            var changedMatchIds = await _context.ChampionMatchupSourceMatches
                .AsNoTracking()
                .Where(source =>
                    source.Patch == patch &&
                    _context.MatchParticipantTimelineSnapshots
                        .IgnoreQueryFilters()
                        .Any(timeline =>
                            timeline.MatchId == source.MatchId &&
                            timeline.MinuteMark == 15 &&
                            (source.LatestTimelineDerivedAtUtc == null ||
                             timeline.DerivedAtUtc > source.LatestTimelineDerivedAtUtc)))
                .OrderBy(source => source.MatchId)
                .Select(source => source.MatchId)
                .Take(batchSize)
                .ToListAsync(ct);
            if (changedMatchIds.Count == 0)
                break;

            await MaterializeMatchupSourceBatchAsync(patch, changedMatchIds, ct);
        }
    }

    private async Task MaterializeMatchupSourceBatchAsync(
        string patch,
        IReadOnlyCollection<Guid> matchIds,
        CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        var now = DateTime.UtcNow;
        var participants = await _context.MatchParticipants
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(participant =>
                matchIds.Contains(participant.MatchId) &&
                participant.TeamPosition != null &&
                participant.TeamPosition != "")
            .Select(participant => new
            {
                participant.MatchId,
                participant.SummonerId,
                participant.ParticipantId,
                participant.TeamId,
                participant.ChampionId,
                Role = participant.TeamPosition!,
                participant.Win
            })
            .ToListAsync(ct);
        var timelineRows = await _context.MatchParticipantTimelineSnapshots
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(timeline => matchIds.Contains(timeline.MatchId) && timeline.MinuteMark == 15)
            .Select(timeline => new
            {
                timeline.MatchId,
                timeline.ParticipantId,
                timeline.Gold,
                timeline.Xp,
                timeline.DerivedAtUtc
            })
            .ToListAsync(ct);
        var timelineByParticipant = timelineRows.ToDictionary(
            timeline => (timeline.MatchId, timeline.ParticipantId));
        var facts = new List<ChampionMatchupFact>(participants.Count);

        foreach (var matchParticipants in participants.GroupBy(participant => participant.MatchId))
        {
            var ordered = matchParticipants.OrderBy(participant => participant.ParticipantId).ToList();
            foreach (var champion in ordered)
            {
                var opponent = ordered.FirstOrDefault(candidate =>
                    candidate.Role == champion.Role && candidate.TeamId != champion.TeamId);
                if (opponent == null)
                    continue;

                var hasChampionTimeline = timelineByParticipant.TryGetValue(
                    (champion.MatchId, champion.ParticipantId),
                    out var championTimeline);
                var hasOpponentTimeline = timelineByParticipant.TryGetValue(
                    (opponent.MatchId, opponent.ParticipantId),
                    out var opponentTimeline);
                var hasTimeline = hasChampionTimeline && hasOpponentTimeline;
                facts.Add(new ChampionMatchupFact
                {
                    Id = Guid.NewGuid(),
                    MatchId = champion.MatchId,
                    ChampionParticipantId = champion.ParticipantId,
                    SummonerId = champion.SummonerId,
                    Patch = patch,
                    ChampionId = champion.ChampionId,
                    Role = champion.Role,
                    OpponentChampionId = opponent.ChampionId,
                    Win = champion.Win,
                    HasTimeline = hasTimeline,
                    GoldDiffAt15 = hasTimeline ? championTimeline!.Gold - opponentTimeline!.Gold : 0,
                    XpDiffAt15 = hasTimeline ? championTimeline!.Xp - opponentTimeline!.Xp : 0,
                    TimelineDerivedAtUtc = hasChampionTimeline ? championTimeline!.DerivedAtUtc : null,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                });
            }
        }

        await ExecuteInTransactionIfNeededAsync(async () =>
        {
            await _context.ChampionMatchupFacts
                .Where(fact => matchIds.Contains(fact.MatchId))
                .ExecuteDeleteAsync(ct);
            _context.ChampionMatchupFacts.AddRange(facts);

            var existing = await _context.ChampionMatchupSourceMatches
                .Where(source => matchIds.Contains(source.MatchId))
                .ToDictionaryAsync(source => source.MatchId, ct);
            foreach (var matchId in matchIds)
            {
                var matchParticipantCount = participants.Count(participant => participant.MatchId == matchId);
                var matchTimeline = timelineRows.Where(timeline => timeline.MatchId == matchId).ToList();
                if (!existing.TryGetValue(matchId, out var source))
                {
                    source = new ChampionMatchupSourceMatch { MatchId = matchId };
                    _context.ChampionMatchupSourceMatches.Add(source);
                }

                source.Patch = patch;
                source.ParticipantCount = matchParticipantCount;
                source.TimelineSnapshotCount = matchTimeline.Count;
                source.LatestTimelineDerivedAtUtc = matchTimeline
                    .Select(timeline => (DateTime?)timeline.DerivedAtUtc)
                    .Max();
                source.ProcessedAtUtc = now;
            }

            await _context.SaveChangesAsync(ct);
        }, ct);

        DetachMatchupBatchEntities(matchIds);
        _telemetry?.RecordSourceBatch(matchIds.Count, facts.Count, stopwatch.Elapsed.TotalMilliseconds);
        _logger.LogInformation(
            "Materialized {Facts} matchup facts from {Matches} source matches for patch {Patch} in {ElapsedMs}ms.",
            facts.Count,
            matchIds.Count,
            patch,
            stopwatch.ElapsedMilliseconds);
    }

    // internal rather than private so the Testcontainers suite can drive the real insert: the
    // SQLite-backed unit tests cannot execute this method's provider-specific SQL.
    internal async Task EnsureRankSnapshotAsync(ChampionMatchupSnapshot snapshot, CancellationToken ct)
    {
        var summonerIds = await _context.ChampionMatchupFacts
            .AsNoTracking()
            .Where(fact => fact.Patch == snapshot.Patch && fact.UpdatedAtUtc <= snapshot.SourceCutoffUtc)
            .Select(fact => fact.SummonerId)
            .Distinct()
            .ToListAsync(ct);
        var existingSummonerIds = await _context.ChampionMatchupRankSnapshots
            .AsNoTracking()
            .Where(row => row.SnapshotId == snapshot.Id)
            .Select(row => row.SummonerId)
            .ToListAsync(ct);
        // The read above is an optimisation, not the correctness guarantee: it keeps a resumed
        // generation from re-sending rows it already wrote. Two runs can still compute overlapping
        // "missing" sets, because nothing serialises them -- a slow database times out the job's
        // reads, Hangfire retries it, and the retry overlaps the original. Both then read the same
        // `existing` set before either inserts, and the second SaveChanges died on
        // PK_ChampionMatchupRankSnapshots (23505), failing the whole generation. Observed on prod at
        // roughly one error an hour, and for six hours straight while the Build Lab modeler was
        // saturating the same database.
        var existing = existingSummonerIds.ToHashSet();
        // Ordered so concurrent writers agree on it. ON CONFLICT DO NOTHING still takes a row lock per
        // key and waits on an in-flight speculative insert of the same key, so two runs inserting the
        // same overlapping set in opposite orders can deadlock -- trading a duplicate-key failure for a
        // deadlock failure. The source query is a DISTINCT with no ORDER BY, whose order is not stable
        // across connections, so the ordering has to be imposed here. Any total order works provided
        // every writer uses the same one.
        var missingSummonerIds = summonerIds
            .Where(summonerId => !existing.Contains(summonerId))
            .OrderBy(summonerId => summonerId)
            .ToList();
        if (missingSummonerIds.Count == 0)
            return;

        foreach (var summonerBatch in missingSummonerIds.Chunk(2_000))
        {
            var ranks = await _context.Ranks
                .AsNoTracking()
                .Where(rank =>
                    rank.QueueType == RankedSoloQueueType &&
                    summonerBatch.Contains(rank.SummonerId))
                .Select(rank => new { rank.SummonerId, rank.Tier })
                .ToDictionaryAsync(rank => rank.SummonerId, rank => rank.Tier, ct);
            // Positional: paired with the batch by index when the VALUES rows are built below, so it
            // must stay aligned with the batch it was projected from.
            var rankTierValues = summonerBatch
                .Select(summonerId => ranks.GetValueOrDefault(summonerId, RankTierCatalog.Unranked))
                .ToArray();

            // ON CONFLICT DO NOTHING rather than EF's AddRange: a concurrent writer racing this batch
            // is then absorbed by the database instead of aborting the batch. The row is identical
            // whichever run writes it -- rank attribution is frozen at the snapshot's cutoff -- so
            // discarding the loser is the correct resolution, not merely the convenient one.
            //
            // A row-per-VALUES list rather than Postgres' unnest, which would express this in three
            // parameters: the matchup equivalence tests run on SQLite, and a provider branch here
            // would mean they stop covering the statement prod actually issues -- which is the gap
            // that let this defect ship. Both providers parse this form. It costs two parameters per
            // row, so a full 2,000-row chunk sends 4,001 against limits of 65,535 (Npgsql) and 32,766
            // (SQLite); chunks that large only ever occur on Postgres.
            var values = new StringBuilder();
            var parameters = new List<object> { snapshot.Id };
            for (var index = 0; index < summonerBatch.Length; index++)
            {
                if (index > 0)
                    values.Append(',');
                values.Append("({0},{").Append(parameters.Count).Append("},{")
                    .Append(parameters.Count + 1).Append("})");
                parameters.Add(summonerBatch[index]);
                parameters.Add(rankTierValues[index]);
            }

            // values holds only "({0},{n},{n+1})" placeholders; every value travels as a parameter.
            var sql = $"""
                 INSERT INTO "ChampionMatchupRankSnapshots" ("SnapshotId", "SummonerId", "RankTier")
                 VALUES {values}
                 ON CONFLICT ("SnapshotId", "SummonerId") DO NOTHING
                 """;
            await _context.Database.ExecuteSqlRawAsync(sql, parameters, ct);
        }
    }

    private async Task AggregateMatchupBatchWithSplitAsync(
        ChampionMatchupSnapshot snapshot,
        IReadOnlyList<int> championIds,
        bool split,
        CancellationToken ct,
        bool extendedTimeout = false)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var grouped = await (
                from fact in _context.ChampionMatchupFacts.AsNoTracking()
                where fact.Patch == snapshot.Patch &&
                      fact.UpdatedAtUtc <= snapshot.SourceCutoffUtc &&
                      championIds.Contains(fact.ChampionId)
                join rank in _context.ChampionMatchupRankSnapshots.AsNoTracking()
                    on new { SnapshotId = snapshot.Id, fact.SummonerId }
                    equals new { rank.SnapshotId, rank.SummonerId }
                group fact by new
                {
                    rank.RankTier,
                    fact.ChampionId,
                    fact.Role,
                    fact.OpponentChampionId
                }
                into groupRows
                select new
                {
                    groupRows.Key.RankTier,
                    groupRows.Key.ChampionId,
                    groupRows.Key.Role,
                    groupRows.Key.OpponentChampionId,
                    Games = groupRows.Count(),
                    Wins = groupRows.Sum(row => row.Win ? 1 : 0),
                    TimelineGames = groupRows.Sum(row => row.HasTimeline ? 1 : 0),
                    SumGoldDiffAt15 = groupRows.Sum(row =>
                        row.HasTimeline ? (long)row.GoldDiffAt15 : 0L),
                    SumXpDiffAt15 = groupRows.Sum(row =>
                        row.HasTimeline ? (long)row.XpDiffAt15 : 0L),
                    LatestTimelineAtUtc = groupRows
                        .Where(row => row.TimelineDerivedAtUtc != null)
                        .Max(row => row.TimelineDerivedAtUtc)
                })
                .ToListAsync(ct);
            var computedAt = DateTime.UtcNow;
            var rows = grouped.Select(group => new ChampionMatchupStat
            {
                Id = Guid.NewGuid(),
                SnapshotId = snapshot.Id,
                Patch = snapshot.Patch,
                RankTier = group.RankTier,
                ChampionId = group.ChampionId,
                Role = group.Role,
                OpponentChampionId = group.OpponentChampionId,
                Games = group.Games,
                Wins = group.Wins,
                TimelineGames = group.TimelineGames,
                SumGoldDiffAt15 = group.SumGoldDiffAt15,
                SumXpDiffAt15 = group.SumXpDiffAt15,
                LatestTimelineAtUtc = group.LatestTimelineAtUtc,
                ComputedAtUtc = computedAt
            }).ToList();

            await ExecuteInTransactionIfNeededAsync(async () =>
            {
                _context.ChampionMatchupStats.AddRange(rows);
                await _context.SaveChangesAsync(ct);
                await _context.ChampionMatchupSnapshots
                    .Where(row => row.Id == snapshot.Id)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(
                            row => row.ProcessedChampionCount,
                            row => row.ProcessedChampionCount + championIds.Count), ct);
            }, ct);

            snapshot.ProcessedChampionCount += championIds.Count;
            foreach (var row in rows)
                _context.Entry(row).State = EntityState.Detached;
            _telemetry?.RecordChampionBatch(
                championIds.Count,
                rows.Count,
                stopwatch.Elapsed.TotalMilliseconds,
                succeeded: true,
                split);
        }
        catch (Exception exception) when (
            exception is not OperationCanceledException &&
            IsTimeout(exception) &&
            championIds.Count > 1)
        {
            DetachAddedMatchupStats(snapshot.Id);
            _telemetry?.RecordChampionBatch(
                championIds.Count,
                0,
                stopwatch.Elapsed.TotalMilliseconds,
                succeeded: false,
                split: true);
            var midpoint = championIds.Count / 2;
            _logger.LogWarning(
                exception,
                "Matchup batch timed out for generation {SnapshotId}; splitting {Count} champions into {Left} and {Right}.",
                snapshot.Id,
                championIds.Count,
                midpoint,
                championIds.Count - midpoint);
            await AggregateMatchupBatchWithSplitAsync(snapshot, championIds.Take(midpoint).ToArray(), split: true, ct);
            await AggregateMatchupBatchWithSplitAsync(snapshot, championIds.Skip(midpoint).ToArray(), split: true, ct);
        }
        catch (Exception exception) when (
            exception is not OperationCanceledException &&
            IsTimeout(exception) &&
            championIds.Count == 1 &&
            !extendedTimeout)
        {
            // One champion cannot split further. Right after a fresh rank snapshot its join reads cold
            // HDD pages (prod: 45s+ cold, 80ms warm), and failing here abandoned whole generations after
            // three attempts. Give it one longer attempt before failing the generation.
            DetachAddedMatchupStats(snapshot.Id);
            var previousTimeout = _context.Database.GetCommandTimeout();
            var extendedSeconds = Math.Clamp(_precomputedOptions.CommandTimeoutSeconds * 4, 60, 600);
            _logger.LogWarning(
                exception,
                "Matchup aggregation for champion {ChampionId} timed out in generation {SnapshotId}; retrying once with a {Timeout}s budget.",
                championIds[0],
                snapshot.Id,
                extendedSeconds);
            _context.Database.SetCommandTimeout(extendedSeconds);
            try
            {
                await AggregateMatchupBatchWithSplitAsync(snapshot, championIds, split: true, ct, extendedTimeout: true);
            }
            finally
            {
                _context.Database.SetCommandTimeout(previousTimeout);
            }
        }
    }

    private async Task PromoteMatchupSnapshotAsync(
        ChampionMatchupSnapshot snapshot,
        int rowCount,
        CancellationToken ct)
    {
        await ExecuteInTransactionIfNeededAsync(async () =>
        {
            await _context.ChampionMatchupSnapshots
                .Where(row => row.Patch == snapshot.Patch && row.IsActive && row.Id != snapshot.Id)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(row => row.IsActive, false)
                    .SetProperty(row => row.Status, ChampionMatchupSnapshotStatus.Retired), ct);

            var completedAt = DateTime.UtcNow;
            await _context.ChampionMatchupSnapshots
                .Where(row => row.Id == snapshot.Id)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(row => row.Status, ChampionMatchupSnapshotStatus.Ready)
                    .SetProperty(row => row.IsActive, true)
                    .SetProperty(row => row.CompletedAtUtc, completedAt)
                    .SetProperty(row => row.ProcessedChampionCount, snapshot.TotalChampionCount)
                    .SetProperty(row => row.FailureReason, (string?)null), ct);

            // Legacy rows remain readable during rollout and are removed only after a complete
            // generation becomes active.
            await _context.ChampionMatchupStats
                .Where(row => row.Patch == snapshot.Patch && row.SnapshotId == null)
                .ExecuteDeleteAsync(ct);
        }, ct);

        snapshot.Status = ChampionMatchupSnapshotStatus.Ready;
        snapshot.IsActive = true;
        snapshot.CompletedAtUtc = DateTime.UtcNow;
        snapshot.ProcessedChampionCount = snapshot.TotalChampionCount;
        _logger.LogInformation(
            "Promoted matchup generation {SnapshotId} for patch {Patch} with {Rows} rows.",
            snapshot.Id,
            snapshot.Patch,
            rowCount);
    }

    private async Task CleanupMatchupSnapshotsAsync(string patch, Guid activeSnapshotId, CancellationToken ct)
    {
        var retained = Math.Clamp(_precomputedOptions.RetainedMatchupGenerations, 1, 10);
        var obsoleteIds = await _context.ChampionMatchupSnapshots
            .AsNoTracking()
            .Where(row =>
                row.Patch == patch &&
                row.Id != activeSnapshotId &&
                row.Status != ChampionMatchupSnapshotStatus.Building)
            .OrderByDescending(row => row.CompletedAtUtc)
            .Skip(retained)
            .Select(row => row.Id)
            .ToListAsync(ct);
        if (obsoleteIds.Count > 0)
        {
            await _context.ChampionMatchupSnapshots
                .Where(row => obsoleteIds.Contains(row.Id))
                .ExecuteDeleteAsync(ct);
        }
    }

    private async Task TrySaveFailureStateAsync(ChampionMatchupSnapshot snapshot, CancellationToken ct)
    {
        try
        {
            await _context.SaveChangesAsync(ct);
        }
        catch (Exception stateException) when (stateException is not OperationCanceledException)
        {
            _logger.LogWarning(
                stateException,
                "Could not persist failure state for matchup generation {SnapshotId}.",
                snapshot.Id);
        }
    }

    private void DetachAddedMatchupStats(Guid snapshotId)
    {
        foreach (var entry in _context.ChangeTracker.Entries<ChampionMatchupStat>()
                     .Where(entry =>
                         entry.State == EntityState.Added &&
                         entry.Entity.SnapshotId == snapshotId))
            entry.State = EntityState.Detached;
    }

    private void DetachMatchupBatchEntities(IReadOnlyCollection<Guid> matchIds)
    {
        foreach (var entry in _context.ChangeTracker.Entries<ChampionMatchupFact>()
                     .Where(entry => matchIds.Contains(entry.Entity.MatchId))
                     .ToList())
            entry.State = EntityState.Detached;
        foreach (var entry in _context.ChangeTracker.Entries<ChampionMatchupSourceMatch>()
                     .Where(entry => matchIds.Contains(entry.Entity.MatchId))
                     .ToList())
            entry.State = EntityState.Detached;
    }

    private static bool IsTimeout(Exception exception) =>
        exception is TimeoutException ||
        exception.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase) ||
        (exception.InnerException != null && IsTimeout(exception.InnerException));

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];

    // ---- ChampionBuildSnapshot: durable per-(champion, role, scope) build response (all-region) ----

}
