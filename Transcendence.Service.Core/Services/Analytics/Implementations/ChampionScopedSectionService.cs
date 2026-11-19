using System.Data;
using System.Data.Common;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Transcendence.Data;
using Transcendence.Data.Models.LoL.Analytics;
using Transcendence.Service.Core.Services.Analytics.Interfaces;
using Transcendence.Service.Core.Services.Analytics.Models;
using Transcendence.Service.Core.Services.Jobs.Configuration;
using Transcendence.Service.Core.Services.Operations;

namespace Transcendence.Service.Core.Services.Analytics.Implementations;

public sealed class ChampionScopedSectionService(
    TranscendenceContext db,
    IChampionBuildComputeService builds,
    IChampionMatchupComputeService matchups,
    IOptions<MatchArchiveOptions> archive,
    IOptions<WorkerJobScheduleOptions> schedule,
    ILogger<ChampionScopedSectionService> logger,
    TimeProvider? timeProvider = null)
{
    internal const string BuildDemand = "profile-build-demand-v1";
    internal const string BuildOutput = "profile-build-result-v1";
    internal const string MatchupDemand = "profile-matchup-demand-v1";
    internal const string MatchupOutput = "profile-matchup-result-v1";

    internal const int PendingLimit = 96;
    internal const int OutputLimitPerSection = 256;
    internal const int MaxPayloadBytes = 128 * 1024;
    internal const int MaxBuildParticipants = 5_000;
    internal const int MaxMatchupPairs = 20_000;
    internal static readonly TimeSpan ResultLifetime = TimeSpan.FromDays(60);
    internal static readonly TimeSpan RefreshInterval = TimeSpan.FromHours(2);
    internal static readonly TimeSpan DemandLifetime = TimeSpan.FromMinutes(45);
    internal static readonly TimeSpan RunBudget = TimeSpan.FromSeconds(40);

    private const int BatchSize = 4;
    private const int CommandTimeoutSeconds = 5;
    private static readonly TimeSpan AttemptBudget = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan AdmissionBudget = TimeSpan.FromMilliseconds(400);

    private DateTime Now => (timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime;
    private IQueryable<AnalyticsResponseSnapshot> Demands => db.AnalyticsResponseSnapshots
        .Where(row => row.Feature == BuildDemand || row.Feature == MatchupDemand);

    public async Task<ChampionScopedSections> GetAsync(
        ChampionSectionScope scope, int scopedRoleGames, CancellationToken ct)
    {
        var normalized = scope.Normalize();
        if (normalized is null)
            return new(Unavailable<ChampionBuildsResponse>(), Unavailable<ChampionMatchupsResponse>());

        var now = Now;
        var key = normalized.Key;
        var rows = await db.AnalyticsResponseSnapshots.AsNoTracking()
            .Where(row => row.ScopeKey == key && row.Patch == normalized.Patch &&
                          (row.Feature == BuildDemand || row.Feature == MatchupDemand ||
                           row.Feature == BuildOutput || row.Feature == MatchupOutput))
            .ToDictionaryAsync(row => row.Feature, ct);

        var build = Latest(await builds.ReadBuildsSnapshotAsync(normalized, ct),
            ReadOutput<ChampionBuildsResponse>(rows.GetValueOrDefault(BuildOutput), now));
        var hasRoles = AnalyticsQueueCatalog.HasRoles(normalized.QueueFamily);
        var matchup = hasRoles
            ? Latest(await matchups.ReadMatchupsSnapshotAsync(normalized, ct),
                ReadOutput<ChampionMatchupsResponse>(rows.GetValueOrDefault(MatchupOutput), now))
            : null;

        var canRequest = scopedRoleGames > 0 && schedule.Value.EnableRefreshRequestedChampionSections &&
                         db.Database.IsNpgsql();
        var buildDemand = PendingState(rows.GetValueOrDefault(BuildDemand), now);
        var matchupDemand = PendingState(rows.GetValueOrDefault(MatchupDemand), now);
        var requestBuilds = canRequest && buildDemand is null && NeedsRefresh(build, now);
        var requestMatchups = canRequest && hasRoles && matchupDemand is null && NeedsRefresh(matchup, now);
        string[] preparing = requestBuilds || requestMatchups
            ? await TryAdmitAsync(normalized, requestBuilds, requestMatchups,
                build is not null, matchup is not null, now, ct)
            : [];

        return new(
            build ?? Missing<ChampionBuildsResponse>(
                canRequest && (buildDemand?.CanRetry == true || preparing.Contains(BuildDemand))),
            matchup ?? Missing<ChampionMatchupsResponse>(
                canRequest && hasRoles && (matchupDemand?.CanRetry == true || preparing.Contains(MatchupDemand))));
    }

    internal static ChampionScopedSection<T>? Latest<T>(
        ChampionScopedSection<T>? canonical, ChampionScopedSection<T>? exact) =>
        canonical is null || exact?.ComputedAtUtc > (canonical.ComputedAtUtc ?? DateTime.MinValue)
            ? exact
            : canonical;

    internal static bool NeedsRefresh<T>(ChampionScopedSection<T>? result, DateTime now) =>
        result?.ComputedAtUtc is not { } at || at < now - RefreshInterval;

    private static AttemptState? PendingState(AnalyticsResponseSnapshot? row, DateTime now) =>
        row is not null && row.ComputedAtUtc > now - DemandLifetime
            ? Deserialize<AttemptState>(row.Payload) ?? AttemptState.Unavailable
            : null;

    private static ChampionScopedSection<T> Unavailable<T>() => Missing<T>(preparing: false);

    private static ChampionScopedSection<T> Missing<T>(bool preparing) =>
        new(preparing ? ChampionSectionState.Preparing : ChampionSectionState.Unavailable, null, default);

    private static ChampionScopedSection<T>? ReadOutput<T>(AnalyticsResponseSnapshot? row, DateTime now)
    {
        if (row is null || row.ComputedAtUtc <= now - ResultLifetime)
            return null;

        var data = Deserialize<T>(row.Payload);
        return data is null ? null : new(ChampionSectionState.Ready, row.ComputedAtUtc, data);
    }

    private async Task<string[]> TryAdmitAsync(
        ChampionSectionScope scope, bool requestBuilds, bool requestMatchups,
        bool hasBuilds, bool hasMatchups, DateTime now, CancellationToken ct)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(AdmissionBudget);
        var token = budget.Token;
        var previousTimeout = db.Database.GetCommandTimeout();
        db.Database.SetCommandTimeout(1);
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(token);
            if (!await TryLockAsync(token))
                return [];

            var isActive = await IsActivePatchAsync(scope.Patch, token);
            requestBuilds &= isActive || !hasBuilds;
            requestMatchups &= isActive || !hasMatchups;
            if ((!requestBuilds && !requestMatchups) ||
                (!isActive && !await IsRetainedHistoryAsync(scope.Patch, token)) ||
                !await HasScopedRoleGamesAsync(scope, token))
                return [];

            var key = scope.Key;
            var pending = await Demands.AsNoTracking()
                .Where(row => row.Patch == scope.Patch && row.ScopeKey == key)
                .ToDictionaryAsync(row => row.Feature, token);
            var occupied = await Demands.CountAsync(token);
            var preparing = new List<string>(2);
            if (requestBuilds)
                occupied = await AdmitOneAsync(scope, BuildDemand, BuildOutput, pending, preparing, occupied, isActive, now, token);
            if (requestMatchups)
                await AdmitOneAsync(scope, MatchupDemand, MatchupOutput, pending, preparing, occupied, isActive, now, token);
            await transaction.CommitAsync(token);
            return preparing.ToArray();
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return [];
        }
        catch (DbException error) when (!ct.IsCancellationRequested)
        {
            logger.LogDebug(error, "Could not admit champion section demand");
            return [];
        }
        finally
        {
            db.Database.SetCommandTimeout(previousTimeout);
        }
    }

    private async Task<int> AdmitOneAsync(
        ChampionSectionScope scope, string feature, string outputFeature,
        IReadOnlyDictionary<string, AnalyticsResponseSnapshot> pending, List<string> preparing,
        int occupied, bool isActive, DateTime now, CancellationToken ct)
    {
        pending.TryGetValue(feature, out var row);
        if (PendingState(row, now) is { } queued)
        {
            if (queued.CanRetry)
                preparing.Add(feature);
            return occupied;
        }

        var key = scope.Key;
        var outputAt = await db.AnalyticsResponseSnapshots.AsNoTracking()
            .Where(item => item.Feature == outputFeature && item.Patch == scope.Patch && item.ScopeKey == key)
            .Select(item => (DateTime?)item.ComputedAtUtc).SingleOrDefaultAsync(ct);
        if (outputAt >= now - RefreshInterval ||
            (outputAt > now - ResultLifetime && !isActive))
            return occupied;

        if (row is not null)
        {
            await Demands.Where(item => item.Id == row.Id).ExecuteDeleteAsync(ct);
            occupied--;
        }

        if (occupied >= PendingLimit)
            return occupied;

        var state = new AttemptState(0, now);
        await InsertAsync(feature, key, scope.Patch, AnalyticsSnapshotSerialization.Serialize(state), now, ct);
        preparing.Add(feature);
        return occupied + 1;
    }

    private Task<bool> IsActivePatchAsync(string patch, CancellationToken ct) =>
        db.Patches.AnyAsync(row => row.Version == patch && row.IsActive, ct);

    private async Task<bool> IsRetainedHistoryAsync(string patch, CancellationToken ct)
    {
        if (!await db.Patches.AnyAsync(row => row.IsActive, ct))
            return false;

        var retained = await MatchRawRetentionPolicy.ProtectedPatchesAsync(db, archive.Value.KeepPatches, ct);
        return retained.Contains(patch);
    }

    private Task<bool> HasScopedRoleGamesAsync(ChampionSectionScope scope, CancellationToken ct)
    {
        var tiers = RankTierCatalog.ResolveScopeTiers(scope.RankTier!);
        var region = AnalyticsRegionCatalog.NormalizeToFilter(scope.Region);
        var rows = db.ChampionRoleTierStats.AsNoTracking()
            .Where(row => row.Patch == scope.Patch && row.QueueFamily == scope.QueueFamily &&
                          row.ChampionId == scope.ChampionId && row.Role == scope.Role && row.Games > 0);
        if (region is not null)
            rows = rows.Where(row => row.PlatformRegion == region);
        if (tiers is not null)
            rows = rows.Where(row => tiers.Contains(row.RankTier));
        return rows.AnyAsync(ct);
    }

    internal async Task DrainAsync(CancellationToken ct)
    {
        if (!schedule.Value.EnableRefreshRequestedChampionSections)
            return;

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(RunBudget);
        var token = budget.Token;
        var previousTimeout = db.Database.GetCommandTimeout();
        db.Database.SetCommandTimeout(CommandTimeoutSeconds);
        try
        {
            var now = Now;
            await PruneAsync(now, token);
            var candidates = await Demands.AsNoTracking()
                .Where(row => row.ComputedAtUtc > now - DemandLifetime)
                .OrderBy(row => row.ComputedAtUtc).ThenBy(row => row.Id)
                .Take(PendingLimit).ToListAsync(token);

            foreach (var (row, state) in candidates
                         .Select(row => (Row: row, State: Deserialize<AttemptState>(row.Payload)))
                         .Where(candidate => candidate.State?.IsDue(now) == true).Take(BatchSize))
                await ProcessAsync(row, state!, token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            logger.LogInformation("Champion section refresh reached its run budget");
        }
        finally
        {
            db.Database.SetCommandTimeout(previousTimeout);
        }
    }

    private async Task ProcessAsync(AnalyticsResponseSnapshot row, AttemptState state, CancellationToken ct)
    {
        var scope = ChampionSectionScope.FromKey(row.Patch, row.ScopeKey);
        var computedAt = Now;
        string? payload;
        try
        {
            using var attempt = CancellationTokenSource.CreateLinkedTokenSource(ct);
            attempt.CancelAfter(AttemptBudget);
            payload = scope is null ? null : await ComputeAsync(scope, row.Feature, attempt.Token);
        }
        catch (Exception error) when (!ct.IsCancellationRequested &&
                                      error is OperationCanceledException or DbException or InvalidOperationException)
        {
            logger.LogWarning(error, "Champion section {Feature} failed for {Patch} {Scope}",
                row.Feature, row.Patch, row.ScopeKey);
            await FinishAsync(row, state.Fail(Now), null, computedAt, ct);
            return;
        }

        if (payload is null || Encoding.UTF8.GetByteCount(payload) > MaxPayloadBytes)
        {
            await FinishAsync(row, AttemptState.Unavailable, null, computedAt, ct);
            return;
        }

        await FinishAsync(row, state, payload, computedAt, ct);
    }

    private async Task<string?> ComputeAsync(ChampionSectionScope scope, string feature, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        if ((!await IsActivePatchAsync(scope.Patch, ct) && !await IsRetainedHistoryAsync(scope.Patch, ct)) ||
            await db.MatchArchiveChunks.AsNoTracking().AnyAsync(row => row.Patch == scope.Patch && row.PrunedMatches > 0, ct) ||
            !await HasScopedRoleGamesAsync(scope, ct))
            return null;

        if (feature == BuildDemand)
        {
            var data = await builds.ComputeScopedBuildsAsync(scope, MaxBuildParticipants, ct);
            return data is null ? null : AnalyticsSnapshotSerialization.Serialize(data);
        }

        if (!AnalyticsQueueCatalog.HasRoles(scope.QueueFamily))
            return null;

        var matchupsData = await matchups.ComputeScopedMatchupsAsync(scope, MaxMatchupPairs, ct);
        return matchupsData is null ? null : AnalyticsSnapshotSerialization.Serialize(matchupsData);
    }

    private async Task FinishAsync(
        AnalyticsResponseSnapshot row, AttemptState state, string? payload, DateTime computedAt, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (!await TryLockAsync(ct))
            return;

        var demand = Demands.Where(item => item.Id == row.Id && item.Payload == row.Payload);
        if (payload is null)
        {
            var next = AnalyticsSnapshotSerialization.Serialize(state);
            await demand.ExecuteUpdateAsync(setters => setters.SetProperty(item => item.Payload, next), ct);
        }
        else if (await demand.ExecuteDeleteAsync(ct) != 0)
        {
            var feature = row.Feature == BuildDemand ? BuildOutput : MatchupOutput;
            var storage = db.AnalyticsResponseSnapshots.Where(item => item.Feature == feature);
            var existing = storage.Where(item => item.Patch == row.Patch && item.ScopeKey == row.ScopeKey);
            if (await existing.ExecuteUpdateAsync(setters => setters
                    .SetProperty(item => item.ComputedAtUtc, computedAt)
                    .SetProperty(item => item.Payload, payload), ct) == 0)
            {
                if (await storage.CountAsync(ct) >= OutputLimitPerSection)
                    await DeleteOldestAsync(storage, 1, ct);
                await InsertAsync(feature, row.ScopeKey, row.Patch, payload, computedAt, ct);
            }
        }

        await transaction.CommitAsync(ct);
    }

    private async Task PruneAsync(DateTime now, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (!await TryLockAsync(ct))
            return;

        await DeleteOldestAsync(Demands.Where(row => row.ComputedAtUtc <= now - DemandLifetime), PendingLimit, ct);
        var results = db.AnalyticsResponseSnapshots
            .Where(row => row.Feature == BuildOutput || row.Feature == MatchupOutput);
        await DeleteOldestAsync(results.Where(row => row.ComputedAtUtc <= now - ResultLifetime), 32, ct);
        await transaction.CommitAsync(ct);
    }

    private Task<int> DeleteOldestAsync(IQueryable<AnalyticsResponseSnapshot> rows, int count, CancellationToken ct)
    {
        var ids = rows.OrderBy(row => row.ComputedAtUtc).ThenBy(row => row.Id).Select(row => row.Id).Take(count);
        return db.AnalyticsResponseSnapshots.Where(row => ids.Contains(row.Id)).ExecuteDeleteAsync(ct);
    }

    private Task<bool> TryLockAsync(CancellationToken ct) =>
        db.Database.SqlQueryRaw<bool>(
            """SELECT pg_try_advisory_xact_lock(hashtextextended('champion-section-demand-v1', 0)) AS "Value" """)
            .SingleAsync(ct);

    private Task<int> InsertAsync(string feature, string key, string patch, string payload, DateTime at, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "AnalyticsResponseSnapshots" ("Id", "Feature", "ScopeKey", "Patch", "Payload", "ComputedAtUtc")
            VALUES ({Guid.NewGuid()}, {feature}, {key}, {patch}, {payload}, {at})
            """, ct);

    private static T? Deserialize<T>(string payload)
    {
        try { return AnalyticsSnapshotSerialization.Deserialize<T>(payload); }
        catch (JsonException) { return default; }
    }

    internal sealed record AttemptState(int Attempts, DateTime NextAttemptAtUtc)
    {
        private const int MaxAttempts = 3;
        public static readonly AttemptState Unavailable = new(MaxAttempts, DateTime.UnixEpoch);

        internal bool CanRetry => Attempts is >= 0 and < MaxAttempts;
        internal bool IsDue(DateTime now) => CanRetry && NextAttemptAtUtc <= now;

        internal AttemptState Fail(DateTime now) =>
            new(Attempts + 1, now.AddMinutes(1 << Attempts));
    }
}
