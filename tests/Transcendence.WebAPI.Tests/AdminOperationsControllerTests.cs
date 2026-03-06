using FluentAssertions;
using Hangfire;
using Hangfire.Common;
using Hangfire.Storage;
using Hangfire.Storage.Monitoring;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Moq;
using Transcendence.Data;
using Transcendence.Data.Models.LoL.Match;
using Transcendence.Data.Models.LoL.Static;
using Transcendence.Service.Core.Services.Analytics.Interfaces;
using Transcendence.Service.Core.Services.Auth.Interfaces;
using Transcendence.Service.Core.Services.Jobs.Configuration;
using Transcendence.Service.Core.Services.RiotApi;
using Transcendence.WebAPI.Controllers;
using DataMatch = Transcendence.Data.Models.LoL.Match.Match;

namespace Transcendence.WebAPI.Tests;

public class AdminOperationsControllerTests
{
    [Fact]
    public async Task GetOverview_ReturnsServerSnapshotsAndEffectiveConcurrency()
    {
        await using var db = CreateDbContext();
        var monitoring = new Mock<IMonitoringApi>();
        monitoring.Setup(x => x.GetStatistics()).Returns(new StatisticsDto
        {
            Enqueued = 12,
            Processing = 3,
            Scheduled = 4,
            Failed = 1,
            Succeeded = 25,
            Recurring = 6,
            Deleted = 2
        });
        monitoring.Setup(x => x.Queues()).Returns(
        [
            new QueueWithTopEnqueuedJobsDto { Name = "refresh-low", Length = 9, Fetched = 1 }
        ]);
        monitoring.Setup(x => x.Servers()).Returns(
        [
            new ServerDto
            {
                Name = "server-a",
                WorkersCount = 15,
                StartedAt = DateTime.UtcNow.AddMinutes(-20),
                Heartbeat = DateTime.UtcNow.AddSeconds(-10),
                Queues = ["refresh-high", "default", "refresh-low"]
            },
            new ServerDto
            {
                Name = "server-b",
                WorkersCount = 10,
                StartedAt = DateTime.UtcNow.AddMinutes(-10),
                Heartbeat = DateTime.UtcNow.AddSeconds(-5),
                Queues = ["default"]
            }
        ]);

        var controller = BuildController(db, monitoring.Object);

        var result = await controller.GetOverview(CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var payload = ok.Value.Should().BeOfType<AdminOverviewResponse>().Subject;
        payload.EffectiveConcurrency.Should().Be(25);
        payload.Servers.Should().HaveCount(2);
        payload.Queues.Should().ContainSingle(x => x.Name == "refresh-low" && x.Length == 9);
        payload.Enqueued.Should().Be(12);
    }

    [Fact]
    public async Task GetJobs_FiltersEnqueuedJobsByRegion()
    {
        await using var db = CreateDbContext();
        var monitoring = new Mock<IMonitoringApi>();
        monitoring.Setup(x => x.Queues()).Returns(
        [
            new QueueWithTopEnqueuedJobsDto { Name = "refresh-low", Length = 2, Fetched = 0 }
        ]);
        monitoring.Setup(x => x.EnqueuedJobs("refresh-low", 0, It.IsAny<int>())).Returns(new JobList<EnqueuedJobDto>(
            new Dictionary<string, EnqueuedJobDto>
            {
                ["job-na"] = new()
                {
                    Job = Job.FromExpression(() => FakeJobs.RunRegion("NA1", CancellationToken.None)),
                    EnqueuedAt = DateTime.UtcNow.AddMinutes(-10),
                    State = "Enqueued",
                    StateData = new Dictionary<string, string>()
                },
                ["job-euw"] = new()
                {
                    Job = Job.FromExpression(() => FakeJobs.RunRegion("EUW1", CancellationToken.None)),
                    EnqueuedAt = DateTime.UtcNow.AddMinutes(-5),
                    State = "Enqueued",
                    StateData = new Dictionary<string, string>()
                }
            }));

        var controller = BuildController(db, monitoring.Object);

        var result = controller.GetJobs(state: "enqueued", region: "NA1", count: 20);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var payload = ok.Value.Should().BeOfType<AdminJobListResponse>().Subject;
        payload.TotalMatched.Should().Be(1);
        payload.Items.Should().ContainSingle(x => x.JobId == "job-na" && x.Region == "NA1");
    }

    [Fact]
    public async Task GetAnalysisMetrics_ReturnsRegionBacklogAndHealth()
    {
        await using var db = CreateDbContext();
        var now = DateTime.UtcNow;
        db.Patches.Add(new Patch
        {
            Version = "15.5",
            ReleaseDate = now.AddHours(-2),
            IsActive = true
        });
        db.Summoners.AddRange(
            new Data.Models.LoL.Account.Summoner
            {
                Id = Guid.NewGuid(),
                PlatformRegion = "NA1",
                Region = "americas",
                Puuid = "na-puuid",
                GameName = "Alpha",
                TagLine = "NA1",
                UpdatedAt = now
            },
            new Data.Models.LoL.Account.Summoner
            {
                Id = Guid.NewGuid(),
                PlatformRegion = "EUW1",
                Region = "europe",
                Puuid = "euw-puuid",
                GameName = "Beta",
                TagLine = "EUW1",
                UpdatedAt = now
            });
        var naMatch = new DataMatch
        {
            Id = Guid.NewGuid(),
            MatchId = "NA_MATCH_1",
            Patch = "15.5",
            PlatformRegion = "NA1",
            QueueId = QueueCatalog.RankedSoloDuoQueueId,
            QueueFamily = QueueCatalog.QueueFamilyRankedSoloDuo,
            QueueType = "ranked",
            MatchDate = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Duration = 1800,
            Status = FetchStatus.Success,
            FetchedAt = now.AddMinutes(-5)
        };
        var euwMatch = new DataMatch
        {
            Id = Guid.NewGuid(),
            MatchId = "EUW_MATCH_1",
            Patch = "15.5",
            PlatformRegion = "EUW1",
            QueueId = QueueCatalog.RankedSoloDuoQueueId,
            QueueFamily = QueueCatalog.QueueFamilyRankedSoloDuo,
            QueueType = "ranked",
            MatchDate = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Duration = 1700,
            Status = FetchStatus.TemporaryFailure
        };
        db.Matches.AddRange(naMatch, euwMatch);
        db.MatchTimelineFetchStates.Add(new MatchTimelineFetchState
        {
            MatchId = naMatch.Id,
            Match = naMatch,
            Status = MatchTimelineFetchStatus.Success,
            LastSuccessAtUtc = now.AddMinutes(-4),
            SourcePatch = "15.5"
        });
        await db.SaveChangesAsync();

        var monitoring = new Mock<IMonitoringApi>();
        monitoring.Setup(x => x.Queues()).Returns(
        [
            new QueueWithTopEnqueuedJobsDto { Name = "refresh-low", Length = 1, Fetched = 0 }
        ]);
        monitoring.Setup(x => x.EnqueuedJobs("refresh-low", 0, It.IsAny<int>())).Returns(new JobList<EnqueuedJobDto>(
            new Dictionary<string, EnqueuedJobDto>
            {
                ["job-euw"] = new()
                {
                    Job = Job.FromExpression(() => FakeJobs.RunRegion("EUW1", CancellationToken.None)),
                    EnqueuedAt = now.AddMinutes(-20),
                    State = "Enqueued",
                    StateData = new Dictionary<string, string>()
                }
            }));
        monitoring.Setup(x => x.ProcessingCount()).Returns(0);
        monitoring.Setup(x => x.ScheduledCount()).Returns(0);

        var controller = BuildController(db, monitoring.Object);

        var result = await controller.GetAnalysisMetrics(CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var payload = ok.Value.Should().BeOfType<AdminAnalysisMetricsResponse>().Subject;
        payload.ActivePatchVersion.Should().Be("15.5");
        payload.Regions.Should().Contain(x =>
            x.Region == "NA1" &&
            x.CurrentPatchSuccessfulMatches == 1 &&
            x.Health == "stale");
        payload.Regions.Should().Contain(x =>
            x.Region == "EUW1" &&
            x.EnqueuedJobs == 1 &&
            x.Health == "blocked");
    }

    private static AdminOperationsController BuildController(
        TranscendenceContext db,
        IMonitoringApi monitoring)
    {
        var storage = new Mock<JobStorage>();
        storage.Setup(x => x.GetMonitoringApi()).Returns(monitoring);
        storage.Setup(x => x.GetConnection()).Returns(Mock.Of<IStorageConnection>());

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OperationalLogs:DirectoryPath"] = "logs"
            })
            .Build();

        var recurringPolicy = new WorkerRecurringJobPolicy(Options.Create(new WorkerSchedulingProfileOptions()));

        return new AdminOperationsController(
            storage.Object,
            configuration,
            Options.Create(new WorkerJobScheduleOptions()),
            Options.Create(new ChampionAnalyticsIngestionJobOptions()),
            Options.Create(new MultiRegionIngestionOptions
            {
                Enabled = true,
                Regions =
                [
                    new() { Region = "NA1", Enabled = true },
                    new() { Region = "EUW1", Enabled = true }
                ]
            }),
            Mock.Of<IRecurringJobManager>(),
            recurringPolicy,
            db,
            Mock.Of<IChampionAnalyticsService>(),
            Mock.Of<IAdminAuditService>())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
    }

    private static TranscendenceContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<TranscendenceContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new TranscendenceContext(options);
    }

    public static class FakeJobs
    {
        public static Task RunRegion(string region, CancellationToken ct = default)
        {
            return Task.CompletedTask;
        }
    }
}
