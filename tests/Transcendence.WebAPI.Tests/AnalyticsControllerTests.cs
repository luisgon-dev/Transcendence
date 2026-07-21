using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;
using Transcendence.Data;
using Transcendence.Data.Models.LoL.Match;
using Transcendence.Data.Models.LoL.Static;
using Transcendence.Service.Core.Services.Analytics.Interfaces;
using Transcendence.Service.Core.Services.Analytics.Implementations;
using Transcendence.Service.Core.Services.Analytics.Models;
using Transcendence.Service.Core.Services.Jobs.Configuration;
using Transcendence.WebAPI.Controllers;
using MatchEntity = Transcendence.Data.Models.LoL.Match.Match;

namespace Transcendence.WebAPI.Tests;

public class AnalyticsControllerTests
{
    [Fact]
    public async Task GetStatus_ReturnsActivePatchMetadata()
    {
        await using var db = CreateDbContext();
        var now = DateTime.UtcNow;
        db.Patches.Add(new Patch
        {
            Version = "16.6",
            ReleaseDate = now.AddHours(-6),
            DetectedAt = now.AddHours(-5),
            IsActive = true
        });
        await db.SaveChangesAsync();

        var controller = new AnalyticsController(
            Mock.Of<IChampionAnalyticsService>(),
            new AnalyticsPatchQueryService(db),
            Options.Create(new MultiRegionIngestionOptions()));

        var result = await controller.GetStatus(CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var payload = ok.Value.Should().BeOfType<AnalyticsPatchStatusDto>().Subject;
        payload.Patch.Should().Be("16.6");
        payload.ActivePatchReleasedAtUtc.Should().NotBeNull();
        payload.ActivePatchDetectedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task GetPatches_ReturnsActivePatchAndPatchesWithSuccessfulRankedMatches()
    {
        await using var db = CreateDbContext();
        var now = DateTime.UtcNow;
        db.Patches.AddRange(
            new Patch { Version = "15.2", ReleaseDate = now.AddHours(-2), DetectedAt = now.AddHours(-1), IsActive = true },
            new Patch { Version = "15.1", ReleaseDate = now.AddDays(-14), DetectedAt = now.AddDays(-14), IsActive = false },
            new Patch { Version = "14.24", ReleaseDate = now.AddDays(-28), DetectedAt = now.AddDays(-28), IsActive = false });
        db.Matches.AddRange(
            new MatchEntity
            {
                Id = Guid.NewGuid(),
                MatchId = "NA1_15_1_ranked",
                Patch = "15.1",
                QueueId = 420,
                QueueType = "RANKED_SOLO_5x5",
                Status = FetchStatus.Success
            },
            new MatchEntity
            {
                Id = Guid.NewGuid(),
                MatchId = "NA1_14_24_ranked_failed",
                Patch = "14.24",
                QueueId = 420,
                QueueType = "RANKED_SOLO_5x5",
                Status = FetchStatus.TemporaryFailure
            },
            new MatchEntity
            {
                Id = Guid.NewGuid(),
                MatchId = "NA1_13_20_ranked",
                Patch = "13.20",
                QueueId = 420,
                QueueType = "RANKED_SOLO_5x5",
                Status = FetchStatus.Success
            });
        await db.SaveChangesAsync();

        var controller = new AnalyticsController(
            Mock.Of<IChampionAnalyticsService>(),
            new AnalyticsPatchQueryService(db),
            Options.Create(new MultiRegionIngestionOptions()));

        var result = await controller.GetPatches(null, CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var payload = ok.Value.Should().BeAssignableTo<IReadOnlyList<AnalyticsPatchOptionDto>>().Subject;
        payload.Select(x => x.Patch).Should().Equal("15.2", "15.1", "13.20");
        payload.First(x => x.Patch == "15.2").IsActive.Should().BeTrue();
        payload.First(x => x.Patch == "15.1").RankedSoloDuoMatchCount.Should().Be(1);
        payload.First(x => x.Patch == "13.20").ReleasedAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task GetPatches_RejectsUnsupportedQueue()
    {
        await using var db = CreateDbContext();
        var controller = new AnalyticsController(
            Mock.Of<IChampionAnalyticsService>(),
            new AnalyticsPatchQueryService(db),
            Options.Create(new MultiRegionIngestionOptions()));

        var result = await controller.GetPatches("normal", CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task GetTierList_ForwardsPatchAndQueueFilters()
    {
        await using var db = CreateDbContext();
        var service = new Mock<IChampionAnalyticsService>();
        service
            .Setup(x => x.GetTierListAsync("TOP", "EMERALD_PLUS", "KR", "aram", "15.1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TierListResponse("15.1", "ALL", "EMERALD_PLUS", "KR", [], QueueFamily: "ARAM"));
        var controller = new AnalyticsController(
            service.Object,
            new AnalyticsPatchQueryService(db),
            Options.Create(new MultiRegionIngestionOptions()));

        var result = await controller.GetTierList("TOP", "EMERALD_PLUS", "KR", "aram", "15.1", CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        service.Verify(x => x.GetTierListAsync("TOP", "EMERALD_PLUS", "KR", "aram", "15.1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetTierList_RejectsUnsupportedQueue()
    {
        await using var db = CreateDbContext();
        var service = new Mock<IChampionAnalyticsService>();
        var controller = new AnalyticsController(
            service.Object,
            new AnalyticsPatchQueryService(db),
            Options.Create(new MultiRegionIngestionOptions()));

        var result = await controller.GetTierList(null, null, null, "normal", null, CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
        service.VerifyNoOtherCalls();
    }

    private static TranscendenceContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<TranscendenceContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new TranscendenceContext(options);
    }
}
