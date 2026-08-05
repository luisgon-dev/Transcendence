using System.Data;
using System.Data.Common;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Options;
using Transcendence.Data;
using Transcendence.Data.Models.LoL.Analytics;
using Transcendence.Data.Models.LoL.Match;
using Transcendence.Service.Core.Services.Analytics.Interfaces;
using Transcendence.Service.Core.Services.Analytics.Models;
using Transcendence.Service.Core.Services.Diagnostics;
using Transcendence.Service.Core.Services.Jobs;
using Transcendence.Service.Core.Services.RiotApi;

namespace Transcendence.Service.Core.Services.Analytics.Implementations;

public sealed class BuildLabGenerationCoordinator(
    TranscendenceContext context,
    HybridCache cache,
    IOptions<BuildLabModelingOptions> optionsAccessor,
    BuildLabTelemetry telemetry,
    ILogger<BuildLabGenerationCoordinator> logger) : IBuildLabGenerationCoordinator
{
    // Serialises every active-pointer flip against the unique active index, whichever process runs it.
    /// <summary>Must match MODELING_LOCK_KEY in the modeler; both hash it with hashtextextended.</summary>
    private const string ModelingLockKey = "build-lab-generation-modeling";

    private const string PromotionLockSql =
        "SELECT pg_advisory_xact_lock(hashtextextended('build-lab-generation-promotion', 0))";
    // Grading rewrites the candidate's whole estimate set and the pointer flip publishes it, so both run
    // under one session-scoped advisory lock: no other promotion may grade or flip in between. It is
    // deliberately session-scoped rather than transaction-scoped so the flip transaction stays short.
    private const string GradingLockResource = "transcendence:build-lab-generation-grading";
    private const string TryAcquireGradingLockSql =
        "SELECT pg_try_advisory_lock(hashtextextended(@resource, 0));";
    private const string ReleaseGradingLockSql =
        "SELECT pg_advisory_unlock(hashtextextended(@resource, 0));";
    private const string GlobalFallbackReason =
        "The regional estimate does not differ meaningfully from the pooled global baseline after multiple-comparison correction.";
    private const string PathGateReason =
        "The complete conditioned path has not passed the sample and interval gates.";
    private const int FailureReasonMaxLength = 1024;
    // The cohort scan and the set-based grading updates sweep the whole match and estimate tables,
    // well past the provider's 30s default. Neither job retries, so a timeout there means no
    // generation is ever created or promoted.
    private const int HeavyCommandTimeoutSeconds = 600;

    private static readonly string[] EmeraldPlusTiers =
        ["EMERALD", "DIAMOND", "MASTER", "GRANDMASTER", "CHALLENGER"];
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly BuildLabModelingOptions options = optionsAccessor.Value;

    public async Task<Guid?> CreatePendingGenerationAsync(CancellationToken ct = default)
    {
        if (!options.Enabled)
            return null;

        await ReapAbandonedModelingRunsAsync(ct);

        var patch = await context.Patches
            .AsNoTracking()
            .Where(row => row.IsActive)
            .Select(row => row.Version)
            .FirstOrDefaultAsync(ct);
        if (string.IsNullOrWhiteSpace(patch))
            return null;

        var alreadyPending = await context.BuildLabGenerations.AnyAsync(generation =>
            generation.Patch == patch &&
            (generation.Status == BuildLabGenerationStatus.PendingDataset ||
             generation.Status == BuildLabGenerationStatus.Modeling ||
             generation.Status == BuildLabGenerationStatus.Candidate), ct);
        if (alreadyPending)
            return null;

        var sourceCutoff = DateTime.UtcNow;
        // The patch set has to be resolved before the count: the modeler reads only these patches,
        // and MatchCount is published as provenance for exactly that cohort.
        var priorPatches = await WithHeavyCommandTimeoutAsync(() => EligibleMatches(sourceCutoff)
            .Where(match => match.Patch != null && match.Patch != "" && match.Patch != patch)
            .GroupBy(match => match.Patch!)
            .Select(group => new { Patch = group.Key, LatestMatch = group.Max(match => match.MatchDate) })
            .OrderByDescending(row => row.LatestMatch)
            .Take(Math.Max(0, options.PriorPatchesToBorrow))
            .Select(row => row.Patch)
            .ToListAsync(ct));
        // The active patch is always in scope, even immediately after a flip when it has the fewest
        // matches of any patch on record.
        List<string> includedPatches = [patch, .. priorPatches];

        var cohort = EligibleMatches(sourceCutoff)
            .Where(match => match.Patch != null && includedPatches.Contains(match.Patch));
        var matchCount = await WithHeavyCommandTimeoutAsync(() => cohort.LongCountAsync(ct));
        if (matchCount == 0)
            return null;

        var regions = await WithHeavyCommandTimeoutAsync(() => cohort
            .Where(match => match.PlatformRegion != null && match.PlatformRegion != "")
            .Select(match => match.PlatformRegion!)
            .Distinct()
            .OrderBy(region => region)
            .ToListAsync(ct));

        var generation = new BuildLabGeneration
        {
            Id = Guid.NewGuid(),
            Status = BuildLabGenerationStatus.PendingDataset,
            Patch = patch,
            RankScope = "EMERALD_PLUS",
            DatasetVersion = options.DatasetVersion,
            StaticDataVersion = patch,
            CodeRevision = options.CodeRevision,
            IncludedPatchesJson = JsonSerializer.Serialize(includedPatches),
            IncludedRegionsJson = JsonSerializer.Serialize(regions),
            SourceCutoffUtc = sourceCutoff,
            MatchCount = matchCount,
            CreatedAtUtc = DateTime.UtcNow
        };
        context.BuildLabGenerations.Add(generation);
        await context.SaveChangesAsync(ct);
        telemetry.RecordGenerationCreated();
        await PublishGenerationGaugesAsync(ct);
        return generation.Id;
    }

    public async Task<int> PromoteReadyCandidatesAsync(CancellationToken ct = default)
    {
        // The recurring schedule flag and Analytics:BuildLab:Enabled are separate keys and prod has
        // been observed with them diverged, so the recurring path guards on Enabled itself. The
        // operator-driven PromoteCandidateAsync deliberately stays reachable for shadow validation.
        if (!options.Enabled)
            return 0;

        await ReapAbandonedModelingRunsAsync(ct);

        var candidates = await context.BuildLabGenerations
            .AsNoTracking()
            .Where(generation => generation.Status == BuildLabGenerationStatus.Candidate)
            .OrderBy(generation => generation.CompletedAtUtc)
            .Select(generation => generation.Id)
            .ToListAsync(ct);
        var promoted = 0;
        foreach (var candidate in candidates)
        {
            try
            {
                if (await PromoteCandidateAsync(candidate, actor: null, ct))
                    promoted++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The job runs with no automatic retry, so one poisoned candidate must not strand
                // the rest of the queue until the next tick.
                context.ChangeTracker.Clear();
                telemetry.RecordPromotionFailed();
                logger.LogError(ex, "Promoting Build Lab generation {GenerationId} failed.", candidate);
            }
        }

        // This tick is the pipeline's regular heartbeat, so the gauges are refreshed here whether or
        // not anything was promoted; the create job only runs daily.
        await PublishGenerationGaugesAsync(ct);
        return promoted;
    }

    public async Task<bool> PromoteCandidateAsync(
        Guid generationId,
        string? actor = null,
        CancellationToken ct = default)
    {
        var generation = await context.BuildLabGenerations
            .FirstOrDefaultAsync(row => row.Id == generationId, ct);
        if (generation == null || generation.Status != BuildLabGenerationStatus.Candidate)
            return false;

        // The v1 floor applies to every generation. DatasetVersion is operator config bound from the
        // same section as the thresholds, so keying this guard on it would make it self-bypassing.
        if (!BuildLabEvidenceGate.UsesV1OrStricterThresholds(options))
        {
            return await RejectAsync(
                generation,
                "Build Lab publication thresholds cannot be lowered below the v1 floor without a new versioned methodology and shadow validation.",
                actor,
                ct);
        }

        if (!ManifestIsPresentAndSelfConsistent(generation))
        {
            return await RejectAsync(
                generation,
                "The candidate generation does not carry a populated, self-consistent model manifest.",
                actor,
                ct);
        }

        if (!TryValidateModel(generation.ValidationMetricsJson, out var failureReason))
            return await RejectAsync(generation, failureReason!, actor, ct);

        // Everything from here on writes publication state, so it is held under one lock: grading and the
        // pointer flip must be mutually exclusive or an overlapping promotion of the same generation can
        // flip the pointer over a half-graded estimate set.
        var gradingLock = await TryAcquireGradingLockAsync(ct);
        if (!gradingLock.Acquired)
        {
            logger.LogInformation(
                "Promoting Build Lab generation {GenerationId} was deferred: another promotion holds the grading lock.",
                generationId);
            return false;
        }

        try
        {
            // Re-read under the lock: a promotion that won the race to it may already have graded and
            // flipped, or failed, this same generation.
            await context.Entry(generation).ReloadAsync(ct);
            if (generation.Status != BuildLabGenerationStatus.Candidate)
                return false;

            await RunWithHeavyCommandTimeoutAsync(() => GradeActionEstimatesAsync(generationId, ct));
            await RunWithHeavyCommandTimeoutAsync(() => GradePathEstimatesAsync(generationId, ct));
            var hasPublishable = await context.AdjustedActionEstimates
                .AnyAsync(estimate => estimate.GenerationId == generationId && estimate.IsPublishable, ct);
            if (!hasPublishable)
                return await RejectAsync(generation, "No action estimate passed the configured publication gates.", actor, ct);

            var now = DateTime.UtcNow;
            await using var transaction = await context.Database.BeginTransactionAsync(ct);
            await context.Database.ExecuteSqlRawAsync(PromotionLockSql, ct);
            await context.BuildLabGenerations
                .Where(row => row.IsActive && row.Id != generationId)
                .ExecuteUpdateAsync(update => update
                    .SetProperty(row => row.IsActive, false)
                    .SetProperty(row => row.Status, BuildLabGenerationStatus.Retired)
                    .SetProperty(row => row.RetiredAtUtc, (DateTime?)now), ct);
            generation.Status = BuildLabGenerationStatus.Ready;
            generation.IsActive = true;
            generation.PromotedAtUtc = now;
            generation.RetiredAtUtc = null;
            generation.CompletedAtUtc ??= now;
            generation.LeaseOwner = null;
            generation.PromotionHistoryJson =
                AppendHistory(generation.PromotionHistoryJson, "promote", actor, null);
            await context.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            telemetry.RecordPromotionSucceeded();
        }
        finally
        {
            await ReleaseGradingLockAsync(gradingLock);
        }

        // Housekeeping only, so it runs once the lock is back and cannot hold up the next promotion.
        await AfterPointerFlipAsync(generationId, retireSuperseded: true, ct);
        return true;
    }

    public async Task<bool> RollbackAsync(
        Guid generationId,
        string? actor = null,
        CancellationToken ct = default)
    {
        var target = await context.BuildLabGenerations
            .FirstOrDefaultAsync(generation => generation.Id == generationId &&
                                               (generation.Status == BuildLabGenerationStatus.Ready ||
                                                generation.Status == BuildLabGenerationStatus.Retired), ct);
        if (target == null)
            return false;

        var now = DateTime.UtcNow;
        await using var transaction = await context.Database.BeginTransactionAsync(ct);
        await context.Database.ExecuteSqlRawAsync(PromotionLockSql, ct);
        await context.BuildLabGenerations
            .Where(generation => generation.IsActive && generation.Id != generationId)
            .ExecuteUpdateAsync(update => update
                .SetProperty(generation => generation.IsActive, false)
                .SetProperty(generation => generation.Status, BuildLabGenerationStatus.Retired)
                .SetProperty(generation => generation.RetiredAtUtc, (DateTime?)now), ct);
        target.IsActive = true;
        target.Status = BuildLabGenerationStatus.Ready;
        target.PromotedAtUtc = now;
        target.RetiredAtUtc = null;
        target.PromotionHistoryJson = AppendHistory(target.PromotionHistoryJson, "rollback", actor, null);
        await context.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        telemetry.RecordRollback();
        await AfterPointerFlipAsync(generationId, retireSuperseded: false, ct);
        return true;
    }

    public async Task<bool> FailGenerationAsync(
        Guid generationId,
        string? reason = null,
        string? actor = null,
        CancellationToken ct = default)
    {
        var generation = await context.BuildLabGenerations
            .FirstOrDefaultAsync(row => row.Id == generationId, ct);
        if (generation == null ||
            (generation.Status != BuildLabGenerationStatus.PendingDataset &&
             generation.Status != BuildLabGenerationStatus.Modeling &&
             generation.Status != BuildLabGenerationStatus.Candidate))
            return false;

        var explanation = string.IsNullOrWhiteSpace(reason)
            ? "An operator abandoned the generation."
            : reason.Trim();
        telemetry.RecordTrainingAbandoned();
        await FailAsync(generation, explanation, actor, ct);
        return true;
    }

    public async Task<BuildLabGenerationAdminResponse> GetAdminStatusAsync(CancellationToken ct = default)
    {
        var generations = await context.BuildLabGenerations
            .AsNoTracking()
            .OrderByDescending(generation => generation.CreatedAtUtc)
            .Take(20)
            .Select(generation => new BuildLabGenerationDto(
                generation.Id,
                generation.Status.ToString(),
                generation.IsActive,
                generation.Patch,
                generation.RankScope,
                generation.DatasetVersion,
                generation.ModelVersion,
                generation.CodeRevision,
                generation.SourceCutoffUtc,
                generation.MatchCount,
                context.AdjustedActionEstimates.LongCount(estimate => estimate.GenerationId == generation.Id),
                context.AdjustedActionEstimates.LongCount(estimate =>
                    estimate.GenerationId == generation.Id && estimate.IsPublishable),
                generation.ArtifactUri,
                generation.ValidationMetricsJson,
                generation.FailureReason,
                generation.LeaseOwner,
                generation.PromotionHistoryJson,
                generation.CreatedAtUtc,
                generation.CompletedAtUtc,
                generation.PromotedAtUtc))
            .ToListAsync(ct);

        var activeId = generations.FirstOrDefault(generation => generation.IsActive)?.Id;
        if (!activeId.HasValue)
            return new BuildLabGenerationAdminResponse(generations, 0, 0);
        var championRoleScopes = await context.AdjustedActionEstimates
            .AsNoTracking()
            .Where(estimate => estimate.GenerationId == activeId && estimate.IsPublishable)
            .Select(estimate => new { estimate.ChampionId, estimate.Role })
            .Distinct()
            .CountAsync(ct);
        var matchupScopes = await context.AdjustedActionEstimates
            .AsNoTracking()
            .Where(estimate => estimate.GenerationId == activeId &&
                               estimate.IsPublishable &&
                               estimate.OpponentChampionId > 0)
            .Select(estimate => new { estimate.ChampionId, estimate.Role, estimate.OpponentChampionId })
            .Distinct()
            .CountAsync(ct);
        return new BuildLabGenerationAdminResponse(generations, championRoleScopes, matchupScopes);
    }

    private IQueryable<Match> EligibleMatches(DateTime sourceCutoff) => context.Matches
        .AsNoTracking()
        .Where(match =>
            match.Status == FetchStatus.Success &&
            match.FetchedAt <= sourceCutoff &&
            match.QueueId == QueueCatalog.RankedSoloDuoQueueId &&
            match.Duration >= 300 &&
            context.MatchTimelineFetchStates.Any(state =>
                state.MatchId == match.Id &&
                state.Status == MatchTimelineFetchStatus.Success &&
                state.SchemaVersion >= MatchTimelineIngestionJob.CurrentTimelineSchemaVersion) &&
            // Emerald+ is a per-participant join in the modeler, so a match only enters the cohort
            // when one of its own participants carries an Emerald+ rank context.
            context.MatchParticipants.Any(participant =>
                participant.MatchId == match.Id &&
                participant.GameEndedInEarlySurrender != true &&
                context.MatchParticipantRankContexts.Any(rank =>
                    rank.MatchId == match.Id &&
                    rank.ParticipantId == participant.ParticipantId &&
                    EmeraldPlusTiers.Contains(rank.Tier))));

    // The flip is already committed, so neither cache invalidation nor retention may turn a
    // completed promotion into a reported failure. Both are housekeeping; the pointer is the record.
    private async Task AfterPointerFlipAsync(Guid generationId, bool retireSuperseded, CancellationToken ct)
    {
        try
        {
            await cache.RemoveByTagAsync("analytics", ct);
            if (retireSuperseded)
                await RetireOldGenerationsAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(
                ex,
                "Housekeeping after the Build Lab active pointer moved to {GenerationId} failed.",
                generationId);
        }
    }

    // Every Build Lab gauge is derived state, so it is snapshotted on the ticks that already touch the
    // generation table rather than by a separate poller. Telemetry may never fail a job: a snapshot
    // error is logged and the previously reported values keep being observed.
    private async Task PublishGenerationGaugesAsync(CancellationToken ct)
    {
        try
        {
            var inFlight = await context.BuildLabGenerations
                .AsNoTracking()
                .Where(generation => generation.Status == BuildLabGenerationStatus.PendingDataset ||
                                     generation.Status == BuildLabGenerationStatus.Modeling ||
                                     generation.Status == BuildLabGenerationStatus.Candidate)
                .Select(generation => new
                {
                    generation.Status,
                    generation.CreatedAtUtc,
                    generation.CompletedAtUtc
                })
                .ToListAsync(ct);
            telemetry.RecordInFlightStatusAges(
                inFlight
                    .Where(row => row.Status == BuildLabGenerationStatus.PendingDataset)
                    .Min(row => (DateTime?)row.CreatedAtUtc),
                // How long the run has been going, which is all the wedge gauge can mean now that
                // liveness is the advisory lock rather than a timestamp.
                inFlight
                    .Where(row => row.Status == BuildLabGenerationStatus.Modeling)
                    .Min(row => (DateTime?)row.CreatedAtUtc),
                inFlight
                    .Where(row => row.Status == BuildLabGenerationStatus.Candidate)
                    .Min(row => (DateTime?)(row.CompletedAtUtc ?? row.CreatedAtUtc)));

            var active = await context.BuildLabGenerations
                .AsNoTracking()
                .Where(generation => generation.IsActive)
                .Select(generation => new
                {
                    generation.Id,
                    generation.PromotedAtUtc,
                    generation.SourceCutoffUtc
                })
                .FirstOrDefaultAsync(ct);
            if (active == null)
            {
                telemetry.RecordActiveGeneration(null, null, 0, 0);
                return;
            }

            // Recounted every tick rather than carried over from promotion: the alert on this gauge
            // exists to catch an already-active generation whose publishable set collapses.
            var publishableActions = await WithHeavyCommandTimeoutAsync(() => context.AdjustedActionEstimates
                .CountAsync(estimate => estimate.GenerationId == active.Id && estimate.IsPublishable, ct));
            var publishablePaths = await WithHeavyCommandTimeoutAsync(() => context.AdjustedPathEstimates
                .CountAsync(path => path.GenerationId == active.Id && path.IsPublishable, ct));
            telemetry.RecordActiveGeneration(
                active.PromotedAtUtc,
                active.SourceCutoffUtc,
                publishableActions,
                publishablePaths);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Refreshing the Build Lab generation gauges failed.");
        }
    }

    // Held for the whole publication step: grading rewrites the candidate's estimate set and the pointer
    // flip publishes it, so the two may not interleave with another promotion. On a non-PostgreSQL provider
    // there is nothing to serialise against (single-process test harness), so the lock is a no-op pass.
    private async Task<GradingLock> TryAcquireGradingLockAsync(CancellationToken ct)
    {
        if (!string.Equals(
                context.Database.ProviderName,
                "Npgsql.EntityFrameworkCore.PostgreSQL",
                StringComparison.Ordinal))
            return new GradingLock(Acquired: true, UsesPostgresAdvisoryLock: false, OpenedConnection: false);

        var connection = context.Database.GetDbConnection();
        // The lock lives on the session, so the connection has to stay open across grading and the flip.
        var openedConnection = connection.State != ConnectionState.Open;
        if (openedConnection)
            await context.Database.OpenConnectionAsync(ct);

        try
        {
            await using var command = CreateGradingLockCommand(connection, TryAcquireGradingLockSql);
            var acquired = await command.ExecuteScalarAsync(ct) is true;
            if (!acquired && openedConnection)
                await context.Database.CloseConnectionAsync();
            return new GradingLock(acquired, UsesPostgresAdvisoryLock: acquired, openedConnection);
        }
        catch
        {
            if (openedConnection)
                await context.Database.CloseConnectionAsync();
            throw;
        }
    }

    private async Task ReleaseGradingLockAsync(GradingLock gradingLock)
    {
        if (!gradingLock.UsesPostgresAdvisoryLock)
            return;

        var connection = context.Database.GetDbConnection();
        try
        {
            await using var command = CreateGradingLockCommand(connection, ReleaseGradingLockSql);
            await command.ExecuteScalarAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            // A session lock is released when the connection closes anyway, so a failed unlock is logged
            // rather than allowed to mask the promotion's own outcome.
            logger.LogError(ex, "Failed to release the Build Lab grading PostgreSQL advisory lock.");
        }
        finally
        {
            if (gradingLock.OpenedConnection)
                await context.Database.CloseConnectionAsync();
        }
    }

    private static DbCommand CreateGradingLockCommand(DbConnection connection, string commandText)
    {
        var command = connection.CreateCommand();
        command.CommandText = commandText;
        var parameter = command.CreateParameter();
        parameter.ParameterName = "resource";
        parameter.Value = GradingLockResource;
        command.Parameters.Add(parameter);
        return command;
    }

    private async Task<T> WithHeavyCommandTimeoutAsync<T>(Func<Task<T>> work)
    {
        var previousTimeout = context.Database.GetCommandTimeout();
        context.Database.SetCommandTimeout(HeavyCommandTimeoutSeconds);
        try
        {
            return await work();
        }
        finally
        {
            context.Database.SetCommandTimeout(previousTimeout);
        }
    }

    // Separately named rather than overloaded: an overload set of Func<Task<T>>/Func<Task> makes
    // every call site depend on lambda return-type inference to pick the right member.
    private Task RunWithHeavyCommandTimeoutAsync(Func<Task> work) =>
        WithHeavyCommandTimeoutAsync(async () =>
        {
            await work();
            return true;
        });

    // Lease contract, shared with the Python modeler: the claimer owns the deadline — it writes
    // LeaseExpiresAtUtc when it claims a generation and moves it forward on every heartbeat — while
    // LeaseTimeoutMinutes is the reaper's outer bound on the interval between heartbeats. Both are
    // enforced here, whichever fires first: a claimer that declared a short expiry and stopped is reaped
    // at that expiry, one that declared a distant expiry and stopped is still reaped at the outer bound,
    // and one that crashed before ever writing an expiry is reaped from its acquisition time.
    /// <summary>
    /// Fails any generation stuck in <c>Modeling</c> with no modeler actually holding the lock.
    /// </summary>
    /// <remarks>
    /// Liveness is decided by PostgreSQL, not by a timeout. The modeler holds a session advisory lock
    /// for the whole run, so acquiring that same lock here is proof no modeler is alive: a crashed,
    /// OOM-killed, or `docker kill`ed process drops its TCP session and the lock with it.
    ///
    /// The previous implementation compared a heartbeat column against
    /// <c>LeaseTimeoutMinutes</c> and reaped six consecutive *healthy* generations, because the
    /// modeler's renewal thread could not win the GIL against a multi-minute pandas load. A lock
    /// probe has no such failure mode — there is no deadline to renew and nothing to schedule — and
    /// it matches how every other long exclusive job here already works.
    /// </remarks>
    private async Task ReapAbandonedModelingRunsAsync(CancellationToken ct)
    {
        var stuck = await context.BuildLabGenerations
            .Where(generation => generation.Status == BuildLabGenerationStatus.Modeling)
            .ToListAsync(ct);
        if (stuck.Count == 0)
            return;

        // Taken on this connection and released immediately: the answer, not the lock, is what matters.
        var probe = await context.Database
            .SqlQuery<bool>($"SELECT pg_try_advisory_lock(hashtextextended({ModelingLockKey}, 0)) AS \"Value\"")
            .SingleAsync(ct);
        if (!probe)
            return;

        try
        {
            foreach (var generation in stuck)
            {
                // Only the modeler moves a generation out of Modeling, so reaching here means a
                // training run died — the fault signal the training-failure alert counts.
                telemetry.RecordTrainingFailed();
                await FailAsync(
                    generation,
                    $"The modeling run held by {generation.LeaseOwner ?? "an unknown owner"} exited without " +
                    "finishing; no modeler holds the modeling lock.",
                    "system",
                    ct);
            }

            logger.LogWarning(
                "Abandoned {GenerationCount} Build Lab generation(s) with no live modeler.",
                stuck.Count);
        }
        finally
        {
            await context.Database.ExecuteSqlAsync(
                $"SELECT pg_advisory_unlock(hashtextextended({ModelingLockKey}, 0))", ct);
        }
    }

    // A gate refusal during promotion, as distinct from a lost training run or a fault: the candidate
    // is failed either way, but only this path is the normal, self-healing outcome.
    private async Task<bool> RejectAsync(
        BuildLabGeneration generation,
        string reason,
        string? actor,
        CancellationToken ct)
    {
        telemetry.RecordPromotionRejected();
        return await FailAsync(generation, reason, actor, ct);
    }

    private async Task<bool> FailAsync(
        BuildLabGeneration generation,
        string reason,
        string? actor,
        CancellationToken ct)
    {
        var clamped = reason.Length > FailureReasonMaxLength ? reason[..FailureReasonMaxLength] : reason;
        generation.Status = BuildLabGenerationStatus.Failed;
        generation.FailureReason = clamped;
        generation.CompletedAtUtc ??= DateTime.UtcNow;
        generation.LeaseOwner = null;
        generation.PromotionHistoryJson =
            AppendHistory(generation.PromotionHistoryJson, "fail", actor, clamped);
        await context.SaveChangesAsync(ct);
        return false;
    }

    // A generation carries hundreds of thousands of estimates, so grading is set-based and runs before the
    // short transaction that flips the active pointer. Every statement below writes a row's final verdict,
    // and the withholding statements run before the publishing ones: grading is never transiently more
    // permissive than its finished state, so a pass that is interrupted, retried, or overlapped by another
    // promotion can only understate what is publishable, never publish a row that failed a gate.
    private async Task GradeActionEstimatesAsync(Guid generationId, CancellationToken ct)
    {
        var estimates = context.AdjustedActionEstimates
            .Where(estimate => estimate.GenerationId == generationId);

        // Applied least significant first so the reason that survives is the first failing gate,
        // matching the order BuildLabEvidenceGate reports.
        foreach (var gate in BuildLabEvidenceGate.ActionGates(options).Reverse())
        {
            await estimates
                .Where(gate.Fails)
                .ExecuteUpdateAsync(update => update
                    .SetProperty(estimate => estimate.IsPublishable, false)
                    .SetProperty(estimate => estimate.EvidenceQuality, "INSUFFICIENT")
                    .SetProperty(estimate => estimate.UnavailableReason, gate.UnavailableReason), ct);
        }

        // Tier before publishability: a row that misses only the interval-width gate can still carry
        // a direction, and that is what keeps the lab populated between patches. Ranking always uses
        // the posterior mean, so this governs presentation only.
        await estimates
            .Where(BuildLabEvidenceGate.QualifiesForBucketedTier(options))
            .ExecuteUpdateAsync(update => update
                .SetProperty(estimate => estimate.EvidenceTier, EvidenceTier.Bucketed), ct);

        var passing = BuildLabEvidenceGate.WherePassesEveryGate(estimates, options);
        await passing.ExecuteUpdateAsync(update => update
            .SetProperty(estimate => estimate.EvidenceTier, EvidenceTier.Numeric), ct);
        // Pooled estimates are graded first and are never withheld in favour of a regional twin, so this is
        // their final verdict and the regional comparison below can read IsPublishable from them directly.
        await passing
            .Where(estimate => estimate.RegionScope == "GLOBAL")
            .ExecuteUpdateAsync(update => update
                .SetProperty(estimate => estimate.IsPublishable, true)
                .SetProperty(estimate => estimate.EvidenceQuality, "PUBLISHABLE")
                .SetProperty(estimate => estimate.UnavailableReason, (string?)null), ct);

        var regional = passing.Where(estimate => estimate.RegionScope != "GLOBAL");
        var correctedCritical = BuildLabEvidenceGate.CorrectedCriticalValue(await regional.CountAsync(ct));
        await BuildLabEvidenceGate
            .WhereMirroredByPublishableGlobal(regional, context.AdjustedActionEstimates, correctedCritical)
            .ExecuteUpdateAsync(update => update
                .SetProperty(estimate => estimate.IsPublishable, false)
                .SetProperty(estimate => estimate.EvidenceQuality, "GLOBAL_FALLBACK")
                .SetProperty(estimate => estimate.UnavailableReason, GlobalFallbackReason), ct);
        // A regional cell with no publishable pooled twin has nothing to fall back to, so it publishes on
        // its own evidence rather than being withheld against a baseline that does not exist.
        await BuildLabEvidenceGate
            .WhereNotMirroredByPublishableGlobal(regional, context.AdjustedActionEstimates, correctedCritical)
            .ExecuteUpdateAsync(update => update
                .SetProperty(estimate => estimate.IsPublishable, true)
                .SetProperty(estimate => estimate.EvidenceQuality, "PUBLISHABLE")
                .SetProperty(estimate => estimate.UnavailableReason, (string?)null), ct);
    }

    private async Task GradePathEstimatesAsync(Guid generationId, CancellationToken ct)
    {
        // Same discipline as the action gates: withhold first, publish second.
        var paths = context.AdjustedPathEstimates.Where(path => path.GenerationId == generationId);
        await paths
            .Where(BuildLabEvidenceGate.PathGateFails(options))
            .ExecuteUpdateAsync(update => update
                .SetProperty(path => path.IsPublishable, false)
                .SetProperty(path => path.UnavailableReason, PathGateReason), ct);
        await paths
            .Where(BuildLabEvidenceGate.PathGatePasses(options))
            .ExecuteUpdateAsync(update => update
                .SetProperty(path => path.IsPublishable, true)
                .SetProperty(path => path.UnavailableReason, (string?)null), ct);
    }

    private static string AppendHistory(string existingJson, string action, string? actor, string? reason)
    {
        List<BuildLabPromotionHistoryEntry>? history = null;
        try
        {
            history = JsonSerializer.Deserialize<List<BuildLabPromotionHistoryEntry>>(existingJson, JsonOptions);
        }
        catch (JsonException)
        {
            // Provenance must never block a promotion: a malformed history restarts rather than throws.
        }

        history ??= [];
        history.Add(new BuildLabPromotionHistoryEntry(action, DateTime.UtcNow, actor, reason));
        return JsonSerializer.Serialize(history, JsonOptions);
    }

    private bool TryValidateModel(string json, out string? failureReason)
    {
        BuildLabValidationMetrics? metrics;
        try
        {
            metrics = JsonSerializer.Deserialize<BuildLabValidationMetrics>(json, JsonOptions);
        }
        catch (JsonException)
        {
            metrics = null;
        }

        if (metrics == null)
        {
            failureReason = "Validation metrics are missing or malformed.";
            return false;
        }

        // A metric the modeler did not report is treated as a failed gate, not as a zero.
        if (!metrics.OverallEce.HasValue ||
            !metrics.MaxTimeBandEce.HasValue ||
            !metrics.BrierScore.HasValue ||
            !metrics.BaselineBrierScore.HasValue ||
            !metrics.LogLoss.HasValue ||
            !metrics.BaselineLogLoss.HasValue ||
            !metrics.HeldOutPatchPassed.HasValue ||
            !metrics.LeakageCheckPassed.HasValue)
        {
            failureReason = "Validation metrics did not report every required calibration, baseline, patch, and leakage field.";
            return false;
        }

        // A single-patch cohort cannot be split across a patch boundary, so there is no test to pass.
        // Reading that absence as a failure blocks every generation until a second patch accumulates
        // coverage. It is safe to waive precisely because the gate guards *borrowing*: with one patch
        // in scope every row carries full weight, no prior-patch row is borrowed, and the staleness the
        // gate exists to catch cannot occur. The chronological holdout still applies -- train,
        // calibration and test remain disjoint match sets ordered by date, and both baseline
        // comparisons below are measured on it.
        var patchHoldoutSatisfied =
            metrics.HeldOutPatchPassed.Value || metrics.HeldOutPatchApplicable == false;
        if (metrics.OverallEce.Value > options.MaximumOverallEce ||
            metrics.MaxTimeBandEce.Value > options.MaximumTimeBandEce ||
            metrics.BrierScore.Value >= metrics.BaselineBrierScore.Value ||
            metrics.LogLoss.Value >= metrics.BaselineLogLoss.Value ||
            !patchHoldoutSatisfied ||
            !metrics.LeakageCheckPassed.Value)
        {
            failureReason = "The candidate model did not pass calibration, baseline, patch, and leakage gates.";
            return false;
        }

        failureReason = null;
        return true;
    }

    // Completeness only: the modeler writes both the manifest and its digest, so this proves the
    // manifest row is populated and internally consistent, not that the artifact bundle matches.
    private static bool ManifestIsPresentAndSelfConsistent(BuildLabGeneration generation)
    {
        if (string.IsNullOrWhiteSpace(generation.ArtifactUri) ||
            string.IsNullOrWhiteSpace(generation.ArtifactSha256) ||
            generation.ArtifactSha256.Length != 64 ||
            string.IsNullOrWhiteSpace(generation.ArtifactManifestJson))
            return false;
        var checksum = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(generation.ArtifactManifestJson)))
            .ToLowerInvariant();
        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(checksum),
            Encoding.ASCII.GetBytes(generation.ArtifactSha256.ToLowerInvariant()));
    }

    // Failed generations are reclaimed alongside retired ones: a rejected candidate keeps its full estimate
    // set, and rejection is the common outcome during shadow validation, so excluding it would leave the
    // table growing without bound.
    private async Task RetireOldGenerationsAsync(CancellationToken ct)
    {
        var keep = Math.Max(2, options.RetainedGenerations);
        var retainedIds = await context.BuildLabGenerations
            .AsNoTracking()
            .Where(generation => generation.Status == BuildLabGenerationStatus.Ready ||
                                 generation.Status == BuildLabGenerationStatus.Retired ||
                                 generation.Status == BuildLabGenerationStatus.Failed)
            // PostgreSQL orders DESC as NULLS FIRST, which would rank never-promoted rows ahead of
            // real promotions and retain the wrong generations. Ordering promoted rows first also keeps
            // failures from ever taking a slot away from a generation that can still be rolled back to.
            .OrderByDescending(generation => generation.PromotedAtUtc != null)
            .ThenByDescending(generation => generation.PromotedAtUtc)
            .ThenByDescending(generation => generation.CreatedAtUtc)
            .Take(keep)
            .Select(generation => generation.Id)
            .ToListAsync(ct);
        // Deletion cascades to the estimate rows, so a generation stays readable for a grace period
        // after it reaches a terminal state rather than vanishing under an in-flight request.
        var deletableBefore = DateTime.UtcNow.AddMinutes(-Math.Max(1, options.RetiredGenerationGraceMinutes));
        await RunWithHeavyCommandTimeoutAsync(() => context.BuildLabGenerations
            .Where(generation => (generation.Status == BuildLabGenerationStatus.Retired ||
                                  generation.Status == BuildLabGenerationStatus.Failed) &&
                                 // Belt and braces: the active generation is Ready, never one of these two.
                                 !generation.IsActive &&
                                 !retainedIds.Contains(generation.Id) &&
                                 // A failure was never promoted or retired, so its grace period runs from
                                 // the moment it was completed.
                                 (generation.RetiredAtUtc ??
                                  generation.PromotedAtUtc ??
                                  generation.CompletedAtUtc ??
                                  generation.CreatedAtUtc) < deletableBefore)
            .ExecuteDeleteAsync(ct));
    }

    private readonly record struct GradingLock(
        bool Acquired,
        bool UsesPostgresAdvisoryLock,
        bool OpenedConnection);
}
