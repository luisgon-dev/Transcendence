using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Transcendence.Data;
using Transcendence.Data.Models.LoL.Static;
using Transcendence.Service.Core.Services.Jobs.Configuration;
using Transcendence.Service.Core.Services.StaticData.Interfaces;
using Transcendence.Service.Core.Tests.Support;
using Transcendence.Service.Workers.Startup;

namespace Transcendence.Service.Core.Tests;

public class StartupPatchRolloverServiceTests
{
    [Fact]
    public async Task PrepareAsync_WhenPatchRolloverDetected_RunsPatchDetectionSynchronously()
    {
        await using var harness = await StartupPatchRolloverHarness.CreateAsync(activePatch: "16.5", latestPatch: "16.6");

        var result = await harness.Service.PrepareAsync(CancellationToken.None);

        result.PatchRolloverDetected.Should().BeTrue();
        result.PatchDetectionCompleted.Should().BeTrue();
        result.PreviousPatch.Should().Be("16.5");
        result.LatestPatch.Should().Be("16.6");
        harness.StaticDataService.DetectAndRefreshCalls.Should().Be(1);
    }

    [Fact]
    public async Task PrepareAsync_WhenActivePatchAlreadyLatest_DoesNothing()
    {
        await using var harness = await StartupPatchRolloverHarness.CreateAsync(activePatch: "16.6", latestPatch: "16.6");

        var result = await harness.Service.PrepareAsync(CancellationToken.None);

        result.PatchRolloverDetected.Should().BeFalse();
        result.PatchDetectionCompleted.Should().BeFalse();
        harness.StaticDataService.DetectAndRefreshCalls.Should().Be(0);
    }

    [Fact]
    public async Task PrepareAsync_WhenBacklogPurgeIsConfigured_DoesNotPurgeQueuedOrScheduledJobs()
    {
        await using var harness = await StartupPatchRolloverHarness.CreateAsync(
            activePatch: "16.5",
            latestPatch: "16.6",
            purgeBacklogOnPatchRolloverOnStartup: true);

        var result = await harness.Service.PrepareAsync(CancellationToken.None);

        result.PurgedEnqueuedJobs.Should().Be(0);
        result.PurgedScheduledJobs.Should().Be(0);
    }

    private sealed class StartupPatchRolloverHarness : IAsyncDisposable
    {
        private StartupPatchRolloverHarness(
            SqliteConnection connection,
            SqliteCompatibleTranscendenceContext db,
            FakeStaticDataService staticDataService,
            ServiceProvider provider,
            StartupPatchRolloverService service)
        {
            Connection = connection;
            Db = db;
            StaticDataService = staticDataService;
            Provider = provider;
            Service = service;
        }

        public SqliteConnection Connection { get; }
        public SqliteCompatibleTranscendenceContext Db { get; }
        public FakeStaticDataService StaticDataService { get; }
        public ServiceProvider Provider { get; }
        public StartupPatchRolloverService Service { get; }

        public static async Task<StartupPatchRolloverHarness> CreateAsync(
            string activePatch,
            string latestPatch,
            bool purgeBacklogOnPatchRolloverOnStartup = false)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();

            var dbOptions = new DbContextOptionsBuilder<TranscendenceContext>()
                .UseSqlite(connection)
                .Options;
            var db = new SqliteCompatibleTranscendenceContext(dbOptions);
            await db.Database.EnsureCreatedAsync();
            db.Patches.Add(new Patch
            {
                Version = activePatch,
                ReleaseDate = DateTime.UtcNow.AddDays(-1),
                DetectedAt = DateTime.UtcNow.AddDays(-1),
                IsActive = true
            });
            await db.SaveChangesAsync();

            var staticDataService = new FakeStaticDataService(latestPatch);
            var services = new ServiceCollection();
            services.AddScoped<TranscendenceContext>(_ => db);
            services.AddScoped<IStaticDataService>(_ => staticDataService);
            var provider = services.BuildServiceProvider();

            var schedule = Options.Create(new WorkerJobScheduleOptions
            {
                RunPatchDetectionOnStartup = true,
                PurgeBacklogOnPatchRolloverOnStartup = purgeBacklogOnPatchRolloverOnStartup
            });

            var service = new StartupPatchRolloverService(
                NullLogger<StartupPatchRolloverService>.Instance,
                provider.GetRequiredService<IServiceScopeFactory>(),
                schedule);

            return new StartupPatchRolloverHarness(connection, db, staticDataService, provider, service);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await Connection.DisposeAsync();
            await Provider.DisposeAsync();
        }
    }

    private sealed class FakeStaticDataService(string latestPatch) : IStaticDataService
    {
        public int DetectAndRefreshCalls { get; private set; }

        public Task<string?> GetLatestPatchVersionAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(latestPatch);

        public Task UpdateStaticDataAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task EnsureStaticDataForPatchAsync(string patchVersion, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task DetectAndRefreshAsync(CancellationToken cancellationToken = default)
        {
            DetectAndRefreshCalls++;
            return Task.CompletedTask;
        }
    }
}
