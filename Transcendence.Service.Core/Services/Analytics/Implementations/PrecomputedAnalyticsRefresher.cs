using System.Diagnostics;
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

/// <summary>
/// Rebuilds the tabular-core precomputed analytics aggregates from raw match data. See
/// <see cref="IPrecomputedAnalyticsRefresher"/>. Every aggregation mirrors the filters of the live
/// compute (the <c>Champion{WinRate,Build,Pro,Matchup}ComputeService</c> services) so the read path
/// can roll these atoms up to the exact same numbers:
/// <list type="bullet">
/// <item><c>ChampionRoleTierStat</c>: a LEFT JOIN to the current solo rank gives each participant a tier
/// ("UNRANKED" when absent); grouped by (region, tier, champion, role) → additive Games/Wins.</item>
/// <item><c>ScopeMatchCountStat</c> / <c>ChampionBanScopeStat</c>: distinct-match denominators/numerators
/// per rank-scope token. These are NOT additive over tier or region, so an explicit synthetic
/// PlatformRegion="ALL" row is materialized (global, no region filter) for the region=ALL read, alongside
/// per-platform rows; the read point-looks-up, never sums. Scope membership uses the same EXISTS form as
/// the live <c>ApplyRankTierScopeToParticipants</c>.</item>
/// </list>
/// Region "ALL" is a reserved synthetic token; <see cref="AllRegion"/>. A null Match.PlatformRegion is
/// coalesced to "" (a bucket only the region=ALL roll-up ever includes).
/// </summary>
public class PrecomputedAnalyticsRefresher : IPrecomputedAnalyticsRefresher
{
    /// <summary>Synthetic PlatformRegion value for the global (region-unfiltered) distinct-match rows.</summary>
    public const string AllRegion = "ALL";

    private const string RankedSoloQueueType = "RANKED_SOLO_5x5";

    /// <summary>Minimum (champion, role) games on a patch before a build snapshot is computed (mirrors the build sample floor).</summary>
    private const int MinBuildGames = 30;

    /// <summary>The rank scopes precomputed for builds: the page default + all-ranks. Specific tiers fall back to raw.</summary>
    private static readonly (string Scope, string? RankTier)[] BuildScopes =
        [(RankTierCatalog.EmeraldPlusScope, RankTierCatalog.EmeraldPlusScope), (RankTierCatalog.AllScope, null)];

    /// <summary>Rank scopes the tier grade is persisted for (region=ALL) — the scopes every web surface
    /// defaults to. Specific tiers (and specific regions) compute on read via the same scorer.</summary>
    private static readonly string[] GradedScopes = [RankTierCatalog.AllScope, RankTierCatalog.EmeraldPlusScope];

    /// <summary>Role partition key for the primary-role "All Roles" overview grade rows.</summary>
    private const string OverviewRole = "ALL";

    private readonly TranscendenceContext _context;
    private readonly IChampionBuildComputeService _buildService;
    private readonly IChampionProComputeService _proService;
    private readonly TieringOptions _tieringOptions;
    private readonly PrecomputedAnalyticsOptions _precomputedOptions;
    private readonly PrecomputedAnalyticsTelemetry? _telemetry;
    private readonly ILogger<PrecomputedAnalyticsRefresher> _logger;

    public PrecomputedAnalyticsRefresher(
        TranscendenceContext context,
        IChampionBuildComputeService buildService,
        IChampionProComputeService proService,
        IOptions<TieringOptions> tieringOptions,
        ILogger<PrecomputedAnalyticsRefresher> logger,
        IOptions<PrecomputedAnalyticsOptions>? precomputedOptions = null,
        PrecomputedAnalyticsTelemetry? telemetry = null)
    {
        _context = context;
        _buildService = buildService;
        _proService = proService;
        _tieringOptions = tieringOptions.Value;
        _precomputedOptions = precomputedOptions?.Value ?? new PrecomputedAnalyticsOptions();
        _telemetry = telemetry;
        _logger = logger;
    }

    public async Task<PrecomputedAnalyticsFullRefreshResult> RefreshAllAsync(string patch, CancellationToken ct)
    {
        var core = new PrecomputedAnalyticsRefreshResult(0, 0, 0, 0);
        var matchupRows = 0;
        var buildRows = 0;
        var proRows = 0;
        var errors = new List<Exception>();
        var coreReady = false;

        // Each surface publishes independently. A matchup timeout must not roll back or indefinitely
        // stale the tabular, build, or pro snapshots that completed successfully.
        try
        {
            core = await RefreshTabularCoreAsync(patch, ct);
            coreReady = true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            errors.Add(exception);
            _logger.LogError(exception, "Precompute refresh (tabular core) failed for patch {Patch}.", patch);
        }
        finally
        {
            _context.ChangeTracker.Clear();
        }

        // Builds depend on the core atoms, so do not publish a build from stale atoms after a core failure.
        if (coreReady)
        {
            try
            {
                buildRows = await RefreshBuildsAsync(patch, ct);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                errors.Add(exception);
                _logger.LogError(exception, "Precompute refresh (builds) failed for patch {Patch}.", patch);
            }
            finally
            {
                _context.ChangeTracker.Clear();
            }
        }

        try
        {
            proRows = await RefreshProSurfacesAsync(patch, ct);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            errors.Add(exception);
            _logger.LogError(exception, "Precompute refresh (pro surfaces) failed for patch {Patch}.", patch);
        }
        finally
        {
            _context.ChangeTracker.Clear();
        }

        try
        {
            matchupRows = await RefreshMatchupsAsync(patch, ct);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            errors.Add(exception);
            _logger.LogError(exception, "Precompute refresh (matchups) failed for patch {Patch}.", patch);
        }
        finally
        {
            _context.ChangeTracker.Clear();
        }

        if (errors.Count > 0)
            throw new AggregateException($"One or more analytics refresh phases failed for patch {patch}.", errors);

        return new PrecomputedAnalyticsFullRefreshResult(core, matchupRows, buildRows, proRows);
    }

    public async Task<PrecomputedAnalyticsRefreshResult> RefreshTabularCoreAsync(string patch, CancellationToken ct)
    {
        var computedAt = DateTime.UtcNow;

        var roleTierRows = new List<ChampionRoleTierStat>();
        var scopeMatchRows = new List<ScopeMatchCountStat>();
        var banRows = new List<ChampionBanScopeStat>();
        var gradeRows = new List<ChampionScopeGradeStat>();
        foreach (var queueFamily in AnalyticsQueueCatalog.SupportedQueueFamilies)
        {
            var queueRoleTierRows = await BuildRoleTierStatsAsync(patch, queueFamily, computedAt, ct);
            var (queueScopeMatchRows, queueBanRows) = await BuildScopeStatsAsync(patch, queueFamily, computedAt, ct);
            roleTierRows.AddRange(queueRoleTierRows);
            scopeMatchRows.AddRange(queueScopeMatchRows);
            banRows.AddRange(queueBanRows);
            gradeRows.AddRange(await BuildScopeGradeStatsAsync(
                patch, queueFamily, computedAt, queueRoleTierRows, queueScopeMatchRows, queueBanRows, ct));
        }
        // Tier grades are scored from the atoms just built (no extra DB round-trip) and persisted in the same
        // transaction so a region=ALL read never sees new atoms paired with a stale/absent grade.

        await ExecuteInTransactionIfNeededAsync(async () =>
        {
            await _context.ChampionRoleTierStats.Where(x => x.Patch == patch).ExecuteDeleteAsync(ct);
            await _context.ScopeMatchCountStats.Where(x => x.Patch == patch).ExecuteDeleteAsync(ct);
            await _context.ChampionBanScopeStats.Where(x => x.Patch == patch).ExecuteDeleteAsync(ct);
            await _context.ChampionScopeGradeStats.Where(x => x.Patch == patch).ExecuteDeleteAsync(ct);

            _context.ChampionRoleTierStats.AddRange(roleTierRows);
            _context.ScopeMatchCountStats.AddRange(scopeMatchRows);
            _context.ChampionBanScopeStats.AddRange(banRows);
            _context.ChampionScopeGradeStats.AddRange(gradeRows);
            await _context.SaveChangesAsync(ct);
        }, ct);

        _logger.LogInformation(
            "Precompute refresh (tabular core) patch {Patch}: {RoleTier} role-tier, {ScopeMatch} scope-match, {Ban} ban, {Grade} grade rows",
            patch, roleTierRows.Count, scopeMatchRows.Count, banRows.Count, gradeRows.Count);

        return new PrecomputedAnalyticsRefreshResult(roleTierRows.Count, scopeMatchRows.Count, banRows.Count, gradeRows.Count);
    }

    // ---- ChampionMatchupStat: resumable immutable generations over narrow durable lane-pair facts ----

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

    private async Task EnsureRankSnapshotAsync(ChampionMatchupSnapshot snapshot, CancellationToken ct)
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
        var existing = existingSummonerIds.ToHashSet();
        var missingSummonerIds = summonerIds.Where(summonerId => !existing.Contains(summonerId)).ToList();
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
            var rows = summonerBatch.Select(summonerId => new ChampionMatchupRankSnapshot
            {
                SnapshotId = snapshot.Id,
                SummonerId = summonerId,
                RankTier = ranks.GetValueOrDefault(summonerId, RankTierCatalog.Unranked)
            }).ToList();
            _context.ChampionMatchupRankSnapshots.AddRange(rows);
            await _context.SaveChangesAsync(ct);
            foreach (var row in rows)
                _context.Entry(row).State = EntityState.Detached;
        }
    }

    private async Task AggregateMatchupBatchWithSplitAsync(
        ChampionMatchupSnapshot snapshot,
        IReadOnlyList<int> championIds,
        bool split,
        CancellationToken ct)
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

    public async Task<int> RefreshBuildsAsync(string patch, CancellationToken ct)
    {
        var computedAt = DateTime.UtcNow;

        // Played (champion, role) pairs with enough games to produce a build (mirrors the build sample floor).
        var pairs = await _context.ChampionRoleTierStats
            .AsNoTracking()
            .Where(x => x.Patch == patch && x.QueueFamily == QueueCatalog.QueueFamilyRankedSoloDuo)
            .GroupBy(x => new { x.ChampionId, x.Role })
            .Select(g => new { g.Key.ChampionId, g.Key.Role, Games = g.Sum(x => x.Games) })
            .Where(x => x.Games >= MinBuildGames)
            .ToListAsync(ct);

        // Compute every (pair, scope) response first (reads), then replace the patch's rows transactionally.
        var rows = new List<ChampionBuildSnapshot>(pairs.Count * BuildScopes.Length);
        foreach (var pair in pairs)
        {
            foreach (var (scope, rankTier) in BuildScopes)
            {
                var response = await _buildService.ComputeBuildsAsync(
                    pair.ChampionId, pair.Role, rankTier, region: null, patch, ct);

                rows.Add(new ChampionBuildSnapshot
                {
                    Patch = patch,
                    ChampionId = pair.ChampionId,
                    Role = pair.Role,
                    RankScope = scope,
                    Payload = BuildSnapshotSerialization.Serialize(response),
                    ComputedAtUtc = computedAt
                });
            }
        }

        await ExecuteInTransactionIfNeededAsync(async () =>
        {
            await _context.ChampionBuildSnapshots.Where(x => x.Patch == patch).ExecuteDeleteAsync(ct);
            _context.ChampionBuildSnapshots.AddRange(rows);
            await _context.SaveChangesAsync(ct);
        }, ct);

        _logger.LogInformation("Precompute refresh (builds) patch {Patch}: {Rows} snapshots ({Pairs} champion-roles)",
            patch, rows.Count, pairs.Count);
        return rows.Count;
    }

    // ---- AnalyticsResponseSnapshot: durable pro-builds + pro-playrate responses (all-region) ----

    public async Task<int> RefreshProSurfacesAsync(string patch, CancellationToken ct)
    {
        var computedAt = DateTime.UtcNow;
        var rows = new List<AnalyticsResponseSnapshot>();

        // Pro-playrate: one response per roster scope, all-region.
        foreach (var scope in AnalyticsSnapshotSerialization.ProScopes)
        {
            var response = await _proService.ComputeProChampionPlayrateAsync(region: null, scope, patch, ct);
            rows.Add(new AnalyticsResponseSnapshot
            {
                Feature = AnalyticsSnapshotSerialization.ProPlayrateFeature,
                ScopeKey = scope,
                Patch = patch,
                Payload = AnalyticsSnapshotSerialization.Serialize(response),
                ComputedAtUtc = computedAt
            });
        }

        // Pro-builds: per pro-played (champion, role) x roster scope, all-region. Enumerate the (champion,
        // role) pairs the active roster (pro OR high-elo) actually plays — much smaller than the general
        // population — and precompute each scope's response (pro/highelo subsets may be empty; that's fine).
        var rosterPuuids = await _context.TrackedProSummoners
            .AsNoTracking()
            .Where(x => x.IsActive && (x.IsPro || x.IsHighEloOtp))
            .Select(x => x.Puuid)
            .Where(p => p != null && p != "")
            .Distinct()
            .ToListAsync(ct);

        var pairs = rosterPuuids.Count == 0
            ? []
            : await BaseParticipants(patch)
                .Where(mp => mp.Puuid != null && rosterPuuids.Contains(mp.Puuid))
                .Select(mp => new { mp.ChampionId, Role = mp.TeamPosition! })
                .Distinct()
                .ToListAsync(ct);

        foreach (var pair in pairs)
        {
            foreach (var scope in AnalyticsSnapshotSerialization.ProScopes)
            {
                var response = await _proService.ComputeProBuildsAsync(
                    pair.ChampionId, region: null, pair.Role, scope, patch, ct);
                rows.Add(new AnalyticsResponseSnapshot
                {
                    Feature = AnalyticsSnapshotSerialization.ProBuildsFeature,
                    ScopeKey = $"{pair.ChampionId}:{pair.Role}:{scope}",
                    Patch = patch,
                    Payload = AnalyticsSnapshotSerialization.Serialize(response),
                    ComputedAtUtc = computedAt
                });
            }
        }

        await ExecuteInTransactionIfNeededAsync(async () =>
        {
            await _context.AnalyticsResponseSnapshots.Where(x => x.Patch == patch).ExecuteDeleteAsync(ct);
            _context.AnalyticsResponseSnapshots.AddRange(rows);
            await _context.SaveChangesAsync(ct);
        }, ct);

        _logger.LogInformation("Precompute refresh (pro surfaces) patch {Patch}: {Rows} snapshots ({Pairs} pro champion-roles)",
            patch, rows.Count, pairs.Count);
        return rows.Count;
    }

    private async Task ExecuteInTransactionIfNeededAsync(Func<Task> action, CancellationToken ct)
    {
        if (_context.Database.CurrentTransaction != null)
        {
            await action();
            return;
        }

        await using var transaction = await _context.Database.BeginTransactionAsync(ct);
        await action();
        await transaction.CommitAsync(ct);
    }

    // ---- ChampionRoleTierStat: per (region, current-tier, champion, role) Games/Wins (additive) ----

    private async Task<List<ChampionRoleTierStat>> BuildRoleTierStatsAsync(
        string patch, string queueFamily, DateTime computedAt, CancellationToken ct)
    {
        var participants = BaseParticipants(patch, queueFamily);
        var ranks = _context.Ranks.AsNoTracking().InAnalyticsRankQueue(queueFamily);
        var hasRoles = AnalyticsQueueCatalog.HasRoles(queueFamily);

        var grouped = await (
            from mp in participants
            join rank in ranks
                on mp.SummonerId equals rank.SummonerId into rankGroup
            from soloRank in rankGroup.DefaultIfEmpty()
            select new
            {
                Region = mp.Match.PlatformRegion,
                Tier = soloRank != null ? soloRank.Tier : RankTierCatalog.Unranked,
                mp.ChampionId,
                Role = hasRoles ? mp.TeamPosition! : AnalyticsQueueCatalog.AllRoles,
                mp.Win
            })
            .GroupBy(x => new { x.Region, x.Tier, x.ChampionId, x.Role })
            .Select(g => new
            {
                g.Key.Region,
                g.Key.Tier,
                g.Key.ChampionId,
                g.Key.Role,
                Games = g.Count(),
                Wins = g.Sum(x => x.Win ? 1 : 0)
            })
            .ToListAsync(ct);

        return grouped
            .Select(g => new ChampionRoleTierStat
            {
                Patch = patch,
                QueueFamily = queueFamily,
                PlatformRegion = g.Region ?? "",
                RankTier = g.Tier,
                ChampionId = g.ChampionId,
                Role = g.Role,
                Games = g.Games,
                Wins = g.Wins,
                ComputedAtUtc = computedAt
            })
            .ToList();
    }

    // ---- ScopeMatchCountStat + ChampionBanScopeStat: distinct-match denominators/numerators per scope ----

    private async Task<(List<ScopeMatchCountStat> ScopeMatches, List<ChampionBanScopeStat> Bans)> BuildScopeStatsAsync(
        string patch, string queueFamily, DateTime computedAt, CancellationToken ct)
    {
        var scopeMatchRows = new List<ScopeMatchCountStat>();
        var banRows = new List<ChampionBanScopeStat>();

        foreach (var scope in RankTierCatalog.RankScopeTokens)
        {
            var scoped = ApplyScope(BaseParticipants(patch, queueFamily), scope, queueFamily);

            // Match region is immutable historical context. Using each participant's current summoner
            // region can split one match into multiple regional denominators after an account transfer.
            var regionMatches = scoped
                .Select(mp => new { Region = mp.Match.PlatformRegion, mp.MatchId })
                .Distinct();

            // Per-platform distinct-match counts.
            var perRegion = await regionMatches
                .GroupBy(x => x.Region)
                .Select(g => new { Region = g.Key, Total = g.Count() })
                .ToListAsync(ct);

            foreach (var r in perRegion)
            {
                scopeMatchRows.Add(new ScopeMatchCountStat
                {
                    Patch = patch,
                    QueueFamily = queueFamily,
                    PlatformRegion = r.Region ?? "",
                    RankScope = scope,
                    TotalMatches = r.Total,
                    ComputedAtUtc = computedAt
                });
            }

            // Global (region=ALL): distinct over the scope ignoring region. Per-region rows are also now
            // single-valued by match, so a transferred participant cannot double-count one match.
            var allTotal = await scoped.Select(mp => mp.MatchId).Distinct().CountAsync(ct);
            if (allTotal > 0)
            {
                scopeMatchRows.Add(new ScopeMatchCountStat
                {
                    Patch = patch,
                    QueueFamily = queueFamily,
                    PlatformRegion = AllRegion,
                    RankScope = scope,
                    TotalMatches = allTotal,
                    ComputedAtUtc = computedAt
                });
            }

            // Ban numerator per (region, champion): distinct banned matches among the scope's matches.
            var bansPerRegion = await (
                from rm in regionMatches
                join b in _context.MatchBans.AsNoTracking() on rm.MatchId equals b.MatchId
                group rm by new { rm.Region, b.ChampionId } into g
                select new
                {
                    g.Key.Region,
                    g.Key.ChampionId,
                    Banned = g.Select(x => x.MatchId).Distinct().Count()
                })
                .ToListAsync(ct);

            foreach (var b in bansPerRegion)
            {
                banRows.Add(new ChampionBanScopeStat
                {
                    Patch = patch,
                    QueueFamily = queueFamily,
                    PlatformRegion = b.Region ?? "",
                    RankScope = scope,
                    ChampionId = b.ChampionId,
                    BannedMatches = b.Banned,
                    ComputedAtUtc = computedAt
                });
            }

            // Global (region=ALL) ban numerator: distinct banned matches over the scope ignoring region.
            var scopedMatchIds = scoped.Select(mp => mp.MatchId).Distinct();
            var bansAll = await _context.MatchBans.AsNoTracking()
                .Where(b => scopedMatchIds.Contains(b.MatchId))
                .GroupBy(b => b.ChampionId)
                .Select(g => new { ChampionId = g.Key, Banned = g.Select(x => x.MatchId).Distinct().Count() })
                .ToListAsync(ct);

            foreach (var b in bansAll)
            {
                banRows.Add(new ChampionBanScopeStat
                {
                    Patch = patch,
                    QueueFamily = queueFamily,
                    PlatformRegion = AllRegion,
                    RankScope = scope,
                    ChampionId = b.ChampionId,
                    BannedMatches = b.Banned,
                    ComputedAtUtc = computedAt
                });
            }
        }

        return (scopeMatchRows, banRows);
    }

    // ---- ChampionScopeGradeStat: the persisted single-source-of-truth tier grade (region=ALL) ----

    /// <summary>
    /// Scores the region=ALL tier grade for the persisted scopes from the atoms just built (rolled up over
    /// all platform regions and the tiers in scope), via the shared <see cref="ChampionTierScorer"/> — the
    /// same scorer the live read path uses, so persisted grades equal on-read grades. Emits per-role rows
    /// plus a primary-role overview row (<c>Role = "ALL"</c>), with movement vs the previous patch.
    /// </summary>
    private async Task<List<ChampionScopeGradeStat>> BuildScopeGradeStatsAsync(
        string patch,
        string queueFamily,
        DateTime computedAt,
        List<ChampionRoleTierStat> roleTierRows,
        List<ScopeMatchCountStat> scopeMatchRows,
        List<ChampionBanScopeStat> banRows,
        CancellationToken ct)
    {
        var previousGrades = await LoadPreviousPatchGradesAsync(patch, queueFamily, ct);
        var gradeRows = new List<ChampionScopeGradeStat>();

        foreach (var scope in GradedScopes)
        {
            var tiersInScope = RankTierCatalog.ResolveScopeTiers(scope); // null => all tiers (includes UNRANKED)

            // region=ALL aggregate: sum atoms across every platform region and the tiers in scope.
            var aggregated = roleTierRows
                .Where(r => tiersInScope == null || tiersInScope.Contains(r.RankTier))
                .GroupBy(r => new { r.ChampionId, r.Role })
                .Select(g => new ChampionTierScorer.RoleGames(
                    g.Key.ChampionId, g.Key.Role, g.Sum(x => x.Games), g.Sum(x => x.Wins)))
                .ToList();
            if (aggregated.Count == 0)
                continue;

            var totalScopeMatches = scopeMatchRows
                .Where(x => x.PlatformRegion == AllRegion && x.RankScope == scope)
                .Select(x => x.TotalMatches)
                .FirstOrDefault();

            var banByChampion = banRows
                .Where(x => x.PlatformRegion == AllRegion && x.RankScope == scope)
                .GroupBy(x => x.ChampionId)
                .ToDictionary(g => g.Key, g => g.First().BannedMatches);

            var score = ChampionTierScorer.ScoreScope(aggregated, banByChampion, totalScopeMatches, _tieringOptions);

            if (AnalyticsQueueCatalog.HasRoles(queueFamily))
            {
                foreach (var s in score.PerRole)
                    gradeRows.Add(MapGrade(patch, queueFamily, scope, s.Role, s, computedAt, previousGrades));
            }
            foreach (var s in score.Overview)
                gradeRows.Add(MapGrade(patch, queueFamily, scope, OverviewRole, s, computedAt, previousGrades));
        }

        return gradeRows;
    }

    /// <summary>Previous patch (by release date) grade tiers keyed by (scope, role, champion), for movement.</summary>
    private async Task<Dictionary<(string Scope, string Role, int ChampionId), int>> LoadPreviousPatchGradesAsync(
        string patch, string queueFamily, CancellationToken ct)
    {
        var currentReleaseDate = await _context.Patches.AsNoTracking()
            .Where(p => p.Version == patch)
            .Select(p => (DateTime?)p.ReleaseDate)
            .FirstOrDefaultAsync(ct);
        if (currentReleaseDate is null)
            return [];

        var previousPatch = await _context.Patches.AsNoTracking()
            .Where(p => p.ReleaseDate < currentReleaseDate.Value)
            .OrderByDescending(p => p.ReleaseDate)
            .Select(p => p.Version)
            .FirstOrDefaultAsync(ct);
        if (string.IsNullOrEmpty(previousPatch))
            return [];

        var rows = await _context.ChampionScopeGradeStats.AsNoTracking()
            .Where(x => x.Patch == previousPatch && x.QueueFamily == queueFamily && x.PlatformRegion == AllRegion)
            .Select(x => new { x.RankScope, x.Role, x.ChampionId, x.Tier })
            .ToListAsync(ct);

        var map = new Dictionary<(string, string, int), int>(rows.Count);
        foreach (var r in rows)
            map[(r.RankScope, r.Role, r.ChampionId)] = r.Tier;
        return map;
    }

    private static ChampionScopeGradeStat MapGrade(
        string patch, string queueFamily, string scope, string roleKey, ChampionTierScorer.ScoredChampion s,
        DateTime computedAt, IReadOnlyDictionary<(string Scope, string Role, int ChampionId), int> previousGrades)
    {
        int? previousTier = previousGrades.TryGetValue((scope, roleKey, s.ChampionId), out var pt) ? pt : null;
        var movement = previousTier.HasValue
            ? (int)ResolveMovement((int)s.Tier, previousTier.Value)
            : (int)TierMovement.NEW;

        return new ChampionScopeGradeStat
        {
            Patch = patch,
            QueueFamily = queueFamily,
            PlatformRegion = AllRegion,
            RankScope = scope,
            Role = roleKey,
            ChampionId = s.ChampionId,
            PrimaryRole = s.Role,
            Tier = (int)s.Tier,
            StrengthScore = s.StrengthScore,
            WinRate = s.WinRate,
            Games = s.Games,
            Wins = s.Wins,
            PickRate = s.PickRate,
            BanRate = s.BanRate,
            ContestedScore = s.ContestedScore,
            RoleBaseline = s.RoleBaseline,
            PriorStrength = s.PriorStrength,
            IsLowSample = s.IsLowSample,
            Movement = movement,
            PreviousTier = previousTier,
            ComputedAtUtc = computedAt
        };
    }

    // TierGrade ints are best→worst (S=0 … D=4), so a smaller new tier means the champion moved UP.
    private static TierMovement ResolveMovement(int newTier, int previousTier) =>
        newTier < previousTier ? TierMovement.UP :
        newTier > previousTier ? TierMovement.DOWN :
        TierMovement.SAME;

    private IQueryable<MatchParticipant> BaseParticipants(
        string patch,
        string queueFamily = QueueCatalog.QueueFamilyRankedSoloDuo) =>
        _context.MatchParticipants
            .AsNoTracking()
            .OnPatch(patch)
            .FromSuccessfulMatches()
            .InAnalyticsQueue(queueFamily)
            .WithAnalyticsRole(queueFamily);

    /// <summary>
    /// Restricts participants to those whose <i>own</i> current solo rank is in the scope, mirroring the
    /// live <c>ApplyRankTierScopeToParticipants</c> EXISTS form. "ALL" applies no filter (includes unranked).
    /// </summary>
    private IQueryable<MatchParticipant> ApplyScope(
        IQueryable<MatchParticipant> query,
        string scope,
        string queueFamily)
    {
        if (scope == RankTierCatalog.AllScope)
            return query;

        var ranks = _context.Ranks.AsNoTracking().InAnalyticsRankQueue(queueFamily);

        if (scope == RankTierCatalog.EmeraldPlusScope)
        {
            return query.Where(mp => ranks.Any(r =>
                r.SummonerId == mp.SummonerId &&
                RankTierCatalog.EmeraldPlusTiers.Contains(r.Tier)));
        }

        // Exact tier.
        return query.Where(mp => ranks.Any(r =>
            r.SummonerId == mp.SummonerId &&
            r.Tier == scope));
    }

}
