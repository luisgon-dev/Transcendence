using System.Diagnostics;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Transcendence.Data;
using Transcendence.Data.Models.LoL.Match;
using Transcendence.Data.Models.LoL.Static;
using Transcendence.Service.Core.Services.Operations;
using Transcendence.Service.Core.Tests.Support;

namespace Transcendence.Service.Core.Tests;

public sealed class MatchArchiveStorageTests
{
    [Fact]
    public void Cascade_catalog_covers_raw_tables_and_preserves_compact_history()
    {
        using var db = new TranscendenceContext(new DbContextOptionsBuilder<TranscendenceContext>()
            .UseNpgsql("Host=localhost;Database=model_only").Options);
        var tables = MatchArchiveCatalog.Build(db.Model, Guid.NewGuid());
        tables.Select(x => x.Name).Should().BeEquivalentTo(
            "Matches", "MatchParticipants", "MatchParticipantItems", "MatchParticipantRunes", "MatchBans",
            "MatchTeamObjectives", "MatchParticipantTimelineSnapshots", "MatchTimelineFetchStates",
            "MatchParticipantItemPurchases", "MatchParticipantSkillOrders", "MatchParticipantItemEvents",
            "MatchParticipantRankContexts");
        tables.Should().OnlyContain(x => x.SelectSql.Contains("MatchArchiveEntries"));
    }

    [Fact]
    public void Managed_archiving_uses_existing_admin_pause_and_resume_controls()
    {
        Transcendence.Service.Core.Services.Admin.Implementations.AdminJobsFacade.IsPausableRecurringJob("archive-matches")
            .Should().BeTrue();
    }

    [Fact]
    public async Task Csv_export_counts_quoted_newlines_and_detects_corruption()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var file = Path.Combine(root, "payload.csv.gz");
            var csv = "id,payload\n1,\"line one\nline two with \"\"quoted\"\" data\"\n2,plain\n";
            var archived = await MatchArchiveFiles.ExportAsync(new StringReader(csv), file, CancellationToken.None);
            archived.Count.Should().Be(2);
            await MatchArchiveFiles.VerifyAsync(file, archived, CancellationToken.None);
            await File.AppendAllTextAsync(file, "corrupt");
            Func<Task> verify = () => MatchArchiveFiles.VerifyAsync(file, archived, CancellationToken.None);
            await verify.Should().ThrowAsync<InvalidDataException>();
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("../../outside")]
    [InlineData("/absolute")]
    public void Archive_paths_cannot_escape_root(string relative)
    {
        Action resolve = () => MatchArchiveFiles.Resolve("/tmp/archive", relative);
        resolve.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void Nas_validation_rejects_a_local_bind_even_below_a_network_parent()
    {
        string[] mounts =
        [
            "1 0 8:1 / / rw - ext4 /dev/root rw",
            "2 1 0:1 / /archive rw - nfs nas:/exports/archive rw",
            "3 2 8:2 /empty /archive/data rw - ext4 /dev/local rw"
        ];
        NetworkMatchArchiveDestination.IsNetworkMount("/archive/good", mounts).Should().BeTrue();
        NetworkMatchArchiveDestination.IsNetworkMount("/archive/data", mounts).Should().BeFalse();
        NetworkMatchArchiveDestination.IsNetworkMount("/tmp/archive", mounts).Should().BeFalse();
    }

    [Fact]
    public async Task Maintenance_gate_interoperates_with_the_Python_backup_flock()
    {
        if (OperatingSystem.IsWindows()) return;
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(Path.Combine(root, "run.lock"), "");
        async Task<int> PythonLock()
        {
            var start = new ProcessStartInfo("python3") { RedirectStandardError = true, UseShellExecute = false };
            start.ArgumentList.Add("-c");
            start.ArgumentList.Add("import fcntl,sys\nf=open(sys.argv[1],'a')\ntry: fcntl.flock(f,fcntl.LOCK_EX|fcntl.LOCK_NB)\nexcept BlockingIOError: sys.exit(42)");
            start.ArgumentList.Add(Path.Combine(root, "run.lock"));
            using var child = Process.Start(start)!;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await child.WaitForExitAsync(timeout.Token);
            return child.ExitCode;
        }
        try
        {
            using (var lease = ArchiveMaintenanceLease.TryAcquire(root))
            {
                lease.Should().NotBeNull();
                (await PythonLock()).Should().Be(42);
                ArchiveMaintenanceLease.TryAcquire(root).Should().BeNull();
            }
            (await PythonLock()).Should().Be(0);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Batch_jobs_share_the_gate_but_foreground_refreshes_are_not_deferred()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(Path.Combine(root, "run.lock"), "");
        try
        {
            using var first = ArchiveMaintenanceLease.TryAcquire(root, exclusive: false);
            using var second = ArchiveMaintenanceLease.TryAcquire(root, exclusive: false);
            first.Should().NotBeNull();
            second.Should().NotBeNull();
            ArchiveMaintenanceLease.TryAcquire(root).Should().BeNull();
            StorageMaintenanceFilter.UsesGate(typeof(Transcendence.Service.Core.Services.Jobs.RefreshChampionSynergyFactsJob)).Should().BeTrue();
            StorageMaintenanceFilter.UsesGate(typeof(Transcendence.Service.Core.Services.Jobs.FetchLatestMatchInformation)).Should().BeFalse();
            StorageMaintenanceFilter.UsesGate(typeof(Transcendence.Service.Core.Services.Jobs.ArchiveMatchesJob)).Should().BeFalse();
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Raw_ingestion_uses_the_same_patch_window_and_keeps_lightweight_tombstones()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new SqliteCompatibleTranscendenceContext(new DbContextOptionsBuilder<TranscendenceContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        await db.Patches.ExecuteUpdateAsync(set => set.SetProperty(x => x.IsActive, false));
        db.Patches.AddRange(
            new Patch { Version = "old-active", IsActive = true, ReleaseDate = DateTime.UtcNow.AddDays(-30) },
            new Patch { Version = "recent", IsActive = false, ReleaseDate = DateTime.UtcNow },
            new Patch { Version = "expired", IsActive = false, ReleaseDate = DateTime.UtcNow.AddDays(-90) });
        await db.SaveChangesAsync();
        var options = new MatchArchiveOptions { Enabled = true, KeepPatches = 1 };
        (await MatchRawRetentionPolicy.ProtectedPatchesAsync(db, 1, CancellationToken.None)).Should().BeEquivalentTo("recent", "old-active");
        var old = new Match { MatchId = "NA1_old", Patch = "expired", Status = FetchStatus.Success };
        (await MatchRawRetentionPolicy.ApplyAsync(db, old, options, CancellationToken.None)).Should().BeFalse();
        old.Status.Should().Be(FetchStatus.OutsideRetentionWindow);
        old.Participants.Should().BeEmpty();
        var active = new Match { MatchId = "NA1_active", Patch = "old-active", Status = FetchStatus.Success };
        (await MatchRawRetentionPolicy.ApplyAsync(db, active, options, CancellationToken.None)).Should().BeTrue();
        active.Status.Should().Be(FetchStatus.Success);
        var unknown = new Match { MatchId = "NA1_new_patch", Patch = "not-in-catalog-yet", Status = FetchStatus.Success };
        (await MatchRawRetentionPolicy.ApplyAsync(db, unknown, options, CancellationToken.None)).Should().BeTrue(
            "a new Riot patch can arrive before static patch detection and must not become a retention tombstone");
    }
}
