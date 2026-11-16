using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using Npgsql;
using Transcendence.Data;
using Transcendence.Data.Models.LoL.Match;
using Transcendence.Data.Models.Service;

namespace Transcendence.Service.Core.Services.Operations;

public sealed class MatchArchiveTelemetry : IDisposable
{
    public const string MeterName = "Transcendence.MatchArchive";
    private readonly Meter meter = new(MeterName);
    private readonly Counter<long> pruned;
    private readonly Counter<long> failures;
    private double lastRun;
    private double pendingAge;
    private long pending;
    private long adoptionRequired;
    public MatchArchiveTelemetry()
    {
        pruned = meter.CreateCounter<long>("match_archive.pruned", "{match}");
        failures = meter.CreateCounter<long>("match_archive.failures", "{run}");
        meter.CreateObservableGauge("match_archive.last_run", () => Volatile.Read(ref lastRun), "s");
        meter.CreateObservableGauge("match_archive.pending", () => Interlocked.Read(ref pending), "{match}");
        meter.CreateObservableGauge("match_archive.pending_age", () => Volatile.Read(ref pendingAge), "s");
        meter.CreateObservableGauge("match_archive.adoption_required", () => Interlocked.Read(ref adoptionRequired));
        pruned.Add(0);
        failures.Add(0);
    }
    public void Observe(int remaining, DateTime? created)
    {
        Interlocked.Exchange(ref pending, remaining);
        Volatile.Write(ref pendingAge, created is null ? 0 : Math.Max(0, (DateTime.UtcNow - created.Value).TotalSeconds));
        Volatile.Write(ref lastRun, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
    }
    public void Pruned(int count) => pruned.Add(count);
    public void Failed() => failures.Add(1);
    public void Tick() => Volatile.Write(ref lastRun, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
    public void AwaitingAdoption(bool required) => Interlocked.Exchange(ref adoptionRequired, required ? 1 : 0);
    public void Dispose() => meter.Dispose();
}

/// <summary>Same local flock as host backup/restore; opening read-only also works through a read-only mount.</summary>
internal static class ArchiveMaintenanceLease
{
    public static async Task<IDisposable?> AcquireExclusiveAsync(string directory, TimeSpan wait, CancellationToken ct)
    {
        var priority = TryFile(Path.Combine(directory, "exclusive.lock"), FileShare.None);
        if (priority is null) return null;
        try
        {
            var started = Stopwatch.StartNew();
            do
            {
                var storage = TryFile(Path.Combine(directory, "run.lock"), FileShare.None);
                if (storage is not null) return new ExclusiveLease(storage, priority);
                await Task.Delay(250, ct);
            } while (started.Elapsed < wait);
            priority.Dispose();
            return null;
        }
        catch { priority.Dispose(); throw; }
    }

    private sealed class ExclusiveLease(FileStream storage, FileStream priority) : IDisposable
    {
        public void Dispose() { storage.Dispose(); priority.Dispose(); }
    }

    public static FileStream? TryAcquire(string directory, bool exclusive = true)
    {
        using var priority = exclusive ? null : TryFile(Path.Combine(directory, "exclusive.lock"), FileShare.Read);
        if (!exclusive && priority is null) return null;
        return TryFile(Path.Combine(directory, "run.lock"), exclusive ? FileShare.None : FileShare.Read);
    }

    private static FileStream? TryFile(string path, FileShare share)
    {
        if (!File.Exists(path)) throw new InvalidOperationException("Archive maintenance volume is not initialized");
        if (AppContext.TryGetSwitch("System.IO.DisableFileLocking", out var disabled) && disabled)
            throw new InvalidOperationException("Archive maintenance requires OS file locking");
        FileStream first;
        try { first = new FileStream(path, FileMode.Open, FileAccess.Read, share); }
        catch (IOException) { return null; }
        // Fail closed if the filesystem/runtime silently ignored the exclusivity request.
        try
        {
            using var second = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
            first.Dispose();
            throw new InvalidOperationException("Archive maintenance filesystem does not enforce exclusive locks");
        }
        catch (IOException) { return first; }
    }
}

public sealed class MatchArchiveRetention(
    TranscendenceContext db,
    IOptions<MatchArchiveOptions> options,
    IMatchArchiveDestination destination,
    MatchArchiveTelemetry telemetry,
    ILogger<MatchArchiveRetention> logger)
{
    private const string AdvisoryName = "transcendence-match-archive-v1";
    private MatchArchiveOptions Settings => options.Value;

    public async Task RunAsync(CancellationToken ct)
    {
        if (!Settings.Enabled) return;
        telemetry.Tick();
        using var maintenance = await ArchiveMaintenanceLease.AcquireExclusiveAsync(Settings.MaintenanceDirectory,
            TimeSpan.FromSeconds(Math.Clamp(Settings.MaxRunSeconds, 1, 3600)), ct);
        if (maintenance is null)
        {
            logger.LogInformation("Match archival deferred while storage maintenance owns the gate.");
            return;
        }
        destination.Verify(Settings.Directory);
        await db.Database.OpenConnectionAsync(ct);
        if (db.Database.GetDbConnection() is not NpgsqlConnection connection)
            throw new InvalidOperationException("Managed match archival requires PostgreSQL");
        var acquired = await db.Database.SqlQueryRaw<bool>(
            $"SELECT pg_try_advisory_lock(hashtextextended('{AdvisoryName}',0)) AS \"Value\"").SingleAsync(ct);
        if (!acquired) { await db.Database.CloseConnectionAsync(); return; }
        var previousTimeout = db.Database.GetCommandTimeout();
        db.Database.SetCommandTimeout(Math.Clamp(Settings.CommandTimeoutSeconds, 1, 600));
        MatchArchiveChunk? chunk = null;
        try
        {
            var started = Stopwatch.StartNew();
            var initialWal = await WalAsync(ct);
            chunk = await db.MatchArchiveChunks.OrderBy(x => x.CreatedAtUtc).FirstOrDefaultAsync(x => x.CompletedAtUtc == null, ct);
            if (chunk is null && await LegacyPendingAsync(ct))
            {
                telemetry.AwaitingAdoption(true);
                logger.LogInformation("Managed archival is waiting for explicit legacy checkpoint adoption; no source rows changed.");
                return;
            }
            telemetry.AwaitingAdoption(false);
            if (!PressureOk()) { logger.LogInformation("Match archival deferred for database I/O pressure."); return; }
            chunk ??= await FreezeAsync(ct);
            if (chunk is null) { telemetry.Observe(0, null); return; }
            if ((await ProtectedPatchesAsync(ct)).Contains(chunk.Patch))
            {
                logger.LogInformation("Archive chunk {ChunkId} is protected by current patch retention; source rows retained.", chunk.Id);
                return;
            }
            var directory = MatchArchiveFiles.Resolve(Settings.Directory, chunk.RelativePath);
            Directory.CreateDirectory(directory);
            var tables = MatchArchiveCatalog.Build(db.Model, chunk.Id);
            var manifest = JsonSerializer.Deserialize<ArchiveManifest>(chunk.ManifestJson, MatchArchiveFiles.Json)
                ?? new ArchiveManifest(chunk.Patch, chunk.FrozenMatches, []);
            if (manifest.Rows is null) manifest = new ArchiveManifest(chunk.Patch, chunk.FrozenMatches, []);
            if (chunk.VerifiedAtUtc is null)
            {
                foreach (var table in tables)
                {
                    if (!await WithinBudgetAsync(started, initialWal, ct)) return;
                    var path = Path.Combine(directory, table.Name + ".csv.gz");
                    if (manifest.Rows.TryGetValue(table.Name, out var existing))
                        await MatchArchiveFiles.VerifyAsync(path, existing, ct);
                    else
                    {
                        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(Settings.CommandTimeoutSeconds, 1, 600)));
                        using (var reader = await connection.BeginTextExportAsync($"COPY ({table.SelectSql}) TO STDOUT WITH (FORMAT CSV, HEADER TRUE)", timeout.Token))
                            manifest.Rows[table.Name] = await MatchArchiveFiles.ExportAsync(reader, path, timeout.Token);
                        chunk.ManifestJson = JsonSerializer.Serialize(manifest, MatchArchiveFiles.Json);
                        await db.SaveChangesAsync(ct);
                        logger.LogInformation("Archive chunk {ChunkId}: verified {Rows} rows in {Table}.", chunk.Id, manifest.Rows[table.Name].Count, table.Name);
                    }
                }
                chunk.ManifestJson = JsonSerializer.Serialize(manifest, MatchArchiveFiles.Json);
                await MatchArchiveFiles.WriteAtomicAsync(Path.Combine(directory, "_manifest.json"), chunk.ManifestJson, ct);
                chunk.VerifiedAtUtc = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
            }
            // Verify every cascade member before EACH prune window; a stale/corrupt NAS is never authority to delete.
            if (!tables.Select(x => x.Name).ToHashSet().SetEquals(manifest.Rows.Keys))
                throw new InvalidDataException("Verified archive no longer covers the complete match cascade");
            foreach (var table in tables)
                await MatchArchiveFiles.VerifyAsync(Path.Combine(directory, table.Name + ".csv.gz"), manifest.Rows[table.Name], ct);
            var batch = Math.Clamp(Settings.DeleteBatch, 1, 250);
            while (await WithinBudgetAsync(started, initialWal, ct))
            {
                var ids = await db.MatchArchiveEntries.Where(x => x.ChunkId == chunk.Id).OrderBy(x => x.MatchId)
                    .Select(x => x.MatchId).Take(batch).ToListAsync(ct);
                if (ids.Count == 0)
                {
                    await MatchArchiveFiles.WriteAtomicAsync(Path.Combine(directory, "_DONE"), DateTime.UtcNow.ToString("O"), ct);
                    chunk.CompletedAtUtc = DateTime.UtcNow;
                    chunk.LastError = null;
                    await db.SaveChangesAsync(ct);
                    telemetry.Observe(0, null);
                    logger.LogInformation("Archive chunk {ChunkId} completed: {Matches} raw matches verified on NAS and pruned.", chunk.Id, chunk.FrozenMatches);
                    return;
                }
                if (await MissingFactsAsync(ids, ct))
                {
                    logger.LogInformation("Archive chunk {ChunkId} is waiting for compact synergy facts; verified raw sources retained.", chunk.Id);
                    return;
                }
                db.Database.SetCommandTimeout(Math.Clamp(Settings.DeleteTimeoutSeconds, 1, 120));
                try
                {
                    await using var transaction = await db.Database.BeginTransactionAsync(ct);
                    // Serialize each small prune with patch promotion, including a new active patch insertion.
                    await db.Database.ExecuteSqlRawAsync("LOCK TABLE \"Patches\" IN SHARE MODE", ct);
                    if ((await ProtectedPatchesAsync(ct)).Contains(chunk.Patch)) return;
                    await db.Matches.IgnoreQueryFilters().Where(x => ids.Contains(x.Id)).ExecuteDeleteAsync(ct);
                    await db.MatchArchiveEntries.Where(x => x.ChunkId == chunk.Id && ids.Contains(x.MatchId)).ExecuteDeleteAsync(ct);
                    if (chunk.LegacySource)
                        await db.Database.ExecuteSqlRawAsync("DELETE FROM _patch_archive_pending WHERE \"Id\"=ANY({0})", new object[] { ids.ToArray() }, ct);
                    chunk.PrunedMatches += ids.Count;
                    chunk.LastError = null;
                    await db.SaveChangesAsync(ct);
                    await transaction.CommitAsync(ct);
                    telemetry.Pruned(ids.Count);
                }
                catch (PostgresException error) when (error.SqlState is "57014" or "55P03" && batch > 1)
                {
                    // The whole transaction has rolled back, including its checkpoint.
                    await db.Entry(chunk).ReloadAsync(ct);
                    batch = Math.Max(1, (batch + 1) / 2);
                    logger.LogInformation("Archive prune yielded to database pressure; next batch is {Batch}.", batch);
                }
                finally { db.Database.SetCommandTimeout(Math.Clamp(Settings.CommandTimeoutSeconds, 1, 600)); }
                if (Settings.BatchDelayMilliseconds > 0)
                    await Task.Delay(Math.Clamp(Settings.BatchDelayMilliseconds, 0, 10_000), ct);
            }
        }
        catch (Exception error) when (!ct.IsCancellationRequested)
        {
            if (chunk is not null)
            {
                chunk.LastError = error.GetType().Name + ": " + error.Message;
                if (chunk.LastError.Length > 1000) chunk.LastError = chunk.LastError[..1000];
                await db.SaveChangesAsync(CancellationToken.None);
            }
            throw;
        }
        finally
        {
            if (chunk is not null && chunk.CompletedAtUtc is null)
            {
                var remaining = await db.MatchArchiveEntries.CountAsync(x => x.ChunkId == chunk.Id, CancellationToken.None);
                telemetry.Observe(remaining, chunk.CreatedAtUtc);
            }
            db.Database.SetCommandTimeout(previousTimeout);
            try { await db.Database.ExecuteSqlRawAsync($"SELECT pg_advisory_unlock(hashtextextended('{AdvisoryName}',0))", CancellationToken.None); }
            finally { await db.Database.CloseConnectionAsync(); }
        }
    }

    private async Task<MatchArchiveChunk?> FreezeAsync(CancellationToken ct)
    {
        var protectedPatches = await ProtectedPatchesAsync(ct);
        var patch = await db.Matches.IgnoreQueryFilters().Where(x => x.Patch != null && x.Patch != "" && !protectedPatches.Contains(x.Patch) &&
                db.Patches.Any(p => p.Version == x.Patch) &&
                x.Status != FetchStatus.OutsideRetentionWindow && (x.Status == FetchStatus.Success || db.MatchParticipants.IgnoreQueryFilters().Any(p => p.MatchId == x.Id)))
            .OrderBy(x => x.Patch).Select(x => x.Patch).FirstOrDefaultAsync(ct);
        if (patch is null) return null;
        var ids = await db.Matches.IgnoreQueryFilters().Where(x => x.Patch == patch && x.Status != FetchStatus.OutsideRetentionWindow).OrderBy(x => x.Id)
            .Select(x => x.Id).Take(Math.Clamp(Settings.FreezeMatches, 1, 500)).ToListAsync(ct);
        if (ids.Count == 0) return null;
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // Match the existing timeline writer's per-match lock. It rechecks this cohort before saving.
        await db.Database.ExecuteSqlRawAsync(
            "SELECT pg_advisory_xact_lock(hashtextextended(\"MatchId\",0)) FROM \"Matches\" WHERE \"Id\"=ANY({0}) ORDER BY \"Id\"",
            new object[] { ids.ToArray() }, ct);
        var chunk = new MatchArchiveChunk { Id = Guid.NewGuid(), Patch = patch, FrozenMatches = ids.Count, CreatedAtUtc = DateTime.UtcNow };
        chunk.RelativePath = Uri.EscapeDataString(patch) + "/chunk-" + chunk.Id.ToString("N");
        chunk.ManifestJson = JsonSerializer.Serialize(new ArchiveManifest(patch, ids.Count, []), MatchArchiveFiles.Json);
        db.MatchArchiveChunks.Add(chunk);
        db.MatchArchiveEntries.AddRange(ids.Select(id => new MatchArchiveEntry { ChunkId = chunk.Id, MatchId = id }));
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return chunk;
    }

    private async Task<List<string>> ProtectedPatchesAsync(CancellationToken ct)
    {
        return await MatchRawRetentionPolicy.ProtectedPatchesAsync(db, Settings.KeepPatches, ct);
    }

    private Task<bool> MissingFactsAsync(List<Guid> ids, CancellationToken ct) => db.Matches.IgnoreQueryFilters().AnyAsync(x =>
        ids.Contains(x.Id) && x.Status == FetchStatus.Success &&
        (x.QueueFamily == "RANKED_FLEX" || x.QueueId == 420 || x.QueueId == 440 || (x.QueueId == 0 && (x.QueueType == "420" || x.QueueType == "440"))) &&
        !db.ChampionSynergySourceMatches.Any(source => source.MatchId == x.Id), ct);

    private async Task<bool> LegacyPendingAsync(CancellationToken ct)
    {
        if (!await db.Database.SqlQueryRaw<bool>("SELECT to_regclass('public._patch_archive_pending') IS NOT NULL AS \"Value\"").SingleAsync(ct)) return false;
        return await db.Database.SqlQueryRaw<bool>("SELECT EXISTS(SELECT 1 FROM _patch_archive_pending LIMIT 1) AS \"Value\"").SingleAsync(ct);
    }

    public async Task AdoptLegacyAsync(string legacyNasRoot, CancellationToken ct)
    {
        using var maintenance = ArchiveMaintenanceLease.TryAcquire(Settings.MaintenanceDirectory)
            ?? throw new InvalidOperationException("Wait for active storage maintenance before adopting archive progress");
        destination.Verify(Settings.Directory);
        var state = Path.Combine(Settings.MaintenanceDirectory, "current");
        if (!File.Exists(state))
        {
            if (await db.MatchArchiveChunks.AnyAsync(x => x.LegacySource, ct)) return;
            throw new InvalidOperationException("No legacy archive state was supplied");
        }
        var lines = await File.ReadAllLinesAsync(state, ct);
        if (lines.Length != 4 || !int.TryParse(lines[2], out var frozen) || frozen < 1 || lines[3] is not ("0" or "1"))
            throw new InvalidDataException("Invalid legacy archive checkpoint");
        var prefix = legacyNasRoot.TrimEnd('/') + '/';
        if (!lines[1].StartsWith(prefix, StringComparison.Ordinal)) throw new InvalidDataException("Unexpected legacy NAS root");
        var relative = lines[1][prefix.Length..];
        var directory = MatchArchiveFiles.Resolve(Settings.Directory, relative);
        var adopted = await db.MatchArchiveChunks.FirstOrDefaultAsync(x => x.LegacySource && x.RelativePath == relative, ct);
        if (adopted is not null)
        {
            if (adopted.Patch != lines[0] || adopted.FrozenMatches != frozen)
                throw new InvalidDataException("Legacy checkpoint differs from its already-adopted owner");
            await RetainAdoptionReceiptAsync(state, adopted.Id, ct);
            return;
        }
        if (await db.MatchArchiveChunks.AnyAsync(x => x.CompletedAtUtc == null, ct))
            throw new InvalidOperationException("An active managed archive chunk already exists");
        if (!await LegacyPendingAsync(ct)) throw new InvalidOperationException("Legacy frozen IDs are missing; inspect before adoption");
        var ids = await db.Database.SqlQueryRaw<Guid>("SELECT \"Id\" AS \"Value\" FROM _patch_archive_pending ORDER BY \"Id\" LIMIT 501").ToListAsync(ct);
        if (ids.Count > frozen || ids.Count > 500) throw new InvalidDataException("Legacy frozen count does not match its checkpoint");
        var chunk = new MatchArchiveChunk
        {
            Id = Guid.NewGuid(), Patch = lines[0], RelativePath = relative, FrozenMatches = frozen,
            PrunedMatches = frozen - ids.Count, CreatedAtUtc = DateTime.UtcNow, LegacySource = true
        };
        if (lines[3] == "1")
        {
            var manifestPath = Path.Combine(directory, "_manifest.json");
            chunk.ManifestJson = await File.ReadAllTextAsync(manifestPath, ct);
            var manifest = JsonSerializer.Deserialize<ArchiveManifest>(chunk.ManifestJson, MatchArchiveFiles.Json)
                ?? throw new InvalidDataException("Missing verified legacy manifest");
            if (manifest.Patch != chunk.Patch || manifest.FrozenMatches != frozen ||
                !MatchArchiveCatalog.Build(db.Model, chunk.Id).Select(x => x.Name).ToHashSet().SetEquals(manifest.Rows.Keys))
                throw new InvalidDataException("Legacy manifest does not cover the current raw-match cascade");
            foreach (var file in manifest.Rows)
                await MatchArchiveFiles.VerifyAsync(Path.Combine(directory, file.Key + ".csv.gz"), file.Value, ct);
            chunk.VerifiedAtUtc = DateTime.UtcNow;
        }
        else
        {
            if (File.Exists(Path.Combine(directory, "_DONE")))
                throw new InvalidDataException("An unverified checkpoint cannot replace a completed NAS chunk");
            if (ids.Count != frozen) throw new InvalidDataException("Unverified legacy archive has already pruned rows");
            chunk.ManifestJson = JsonSerializer.Serialize(new ArchiveManifest(chunk.Patch, frozen, []), MatchArchiveFiles.Json);
        }
        var actual = await db.Matches.IgnoreQueryFilters().Where(x => ids.Contains(x.Id)).Select(x => x.Patch).ToListAsync(ct);
        if (actual.Count != ids.Count || actual.Any(patch => patch != chunk.Patch))
            throw new InvalidDataException("Legacy frozen IDs no longer match their owned patch");
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync(
            "SELECT pg_advisory_xact_lock(hashtextextended(\"MatchId\",0)) FROM \"Matches\" WHERE \"Id\"=ANY({0}) ORDER BY \"Id\"",
            new object[] { ids.ToArray() }, ct);
        db.MatchArchiveChunks.Add(chunk);
        db.MatchArchiveEntries.AddRange(ids.Select(id => new MatchArchiveEntry { ChunkId = chunk.Id, MatchId = id }));
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await RetainAdoptionReceiptAsync(state, chunk.Id, ct);
        logger.LogInformation("Adopted legacy archive chunk {ChunkId}: {Remaining}/{Frozen} frozen matches; NAS files retained.", chunk.Id, ids.Count, frozen);
    }

    private static async Task RetainAdoptionReceiptAsync(string state, Guid owner, CancellationToken ct)
    {
        var receipt = state + ".adopted-" + owner.ToString("N");
        if (File.Exists(receipt))
        {
            var input = await File.ReadAllBytesAsync(state, ct);
            var retained = await File.ReadAllBytesAsync(receipt, ct);
            if (!input.SequenceEqual(retained))
                throw new InvalidDataException("Legacy state conflicts with the retained adoption receipt");
            File.Delete(state); // Exact duplicate of retained recovery evidence, after owner validation.
        }
        else File.Move(state, receipt);
    }

    private async Task<ulong> WalAsync(CancellationToken ct)
    {
        var value = await db.Database.SqlQueryRaw<string>("SELECT pg_current_wal_insert_lsn()::text AS \"Value\"").SingleAsync(ct);
        var parts = value.Split('/');
        return (Convert.ToUInt64(parts[0], 16) << 32) | Convert.ToUInt64(parts[1], 16);
    }
    private async Task<bool> WithinBudgetAsync(Stopwatch started, ulong initialWal, CancellationToken ct) =>
        started.Elapsed.TotalSeconds < Math.Clamp(Settings.MaxRunSeconds, 1, 3600) && PressureOk() &&
        await WalAsync(ct) - initialWal < (ulong)Math.Clamp(Settings.MaxWalMegabytesPerRun, 1, 4096) * 1024 * 1024;

    private bool PressureOk()
    {
        if (!File.Exists(Settings.IoPressureFile)) return true;
        var full = File.ReadLines(Settings.IoPressureFile).FirstOrDefault(x => x.StartsWith("full ", StringComparison.Ordinal));
        var value = full?.Split(' ').FirstOrDefault(x => x.StartsWith("avg60=", StringComparison.Ordinal))?[6..];
        return value is not null && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var pressure) &&
               pressure < Math.Clamp(Settings.MaxIoPressure, 1, 100);
    }
}
