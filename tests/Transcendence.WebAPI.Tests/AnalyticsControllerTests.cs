using System.Text.Json;
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
using Transcendence.WebAPI.Models;
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
            Options.Create(new MultiRegionIngestionOptions()),
            new AnalyticsPublicationService(db));

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
            Options.Create(new MultiRegionIngestionOptions()),
            new AnalyticsPublicationService(db));

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
            Options.Create(new MultiRegionIngestionOptions()),
            new AnalyticsPublicationService(db));

        var result = await controller.GetPatches("normal", CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetTierList_ReturnsRankedBoardForRequestedPatchAndQueue(bool includeLowSample)
    {
        await using var db = CreateDbContext();
        var service = new Mock<IChampionAnalyticsService>(MockBehavior.Strict);
        var publicationService = new Mock<IAnalyticsPublicationService>(MockBehavior.Strict);
        var publication = new AnalyticsPublication(new(AnalyticsPatchSelectionState.Ready, "15.2", "15.1", "15.1"));
        var sample = new AnalyticsSampleMetadata(AnalyticsSampleStatus.Sufficient, 528, 40, 48, false,
            AnalyticsPatchPhase.Maturing, false);
        var scope = new TierListResponse("15.1", "ALL", "EMERALD_PLUS", "KR",
            [
                new(4, "ALL", TierGrade.B, 0, 1, 0.01, 0.01, 8, null, null, IsLowSample: true),
                new(2, "ALL", TierGrade.S, 0.02, 0.54, 0.1, 0.05, 200, null, null),
                new(1, "ALL", TierGrade.S, 0.01, 0.56, 0.1, 0.05, 200, null, null),
                new(3, "ALL", TierGrade.C, -0.01, 0.49, 0.05, 0.02, 120, null, null)
            ],
            sample, QueueFamily: "ARAM", Confidence: TierScopeConfidence.RESOLVED, PatchSelection: publication.Selection);
        publicationService.Setup(x => x.ResolveAsync("aram", "15.1", It.IsAny<CancellationToken>(), "EMERALD_PLUS"))
            .ReturnsAsync(publication);
        service
            .Setup(x => x.GetPublishedTierListAsync("TOP", "EMERALD_PLUS", "KR", "aram", publication, It.IsAny<CancellationToken>()))
            .ReturnsAsync(scope);
        var controller = new AnalyticsController(
            service.Object,
            new AnalyticsPatchQueryService(db),
            Options.Create(new MultiRegionIngestionOptions()),
            publicationService.Object);

        var result = includeLowSample
            ? await controller.GetTierList("TOP", "EMERALD_PLUS", "KR", "aram", "15.1", includeLowSample: true)
            : await controller.GetTierList("TOP", "EMERALD_PLUS", "KR", "aram", "15.1");

        var payload = result.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<TierListBoardResponse>().Subject;
        payload.PatchSelection.Should().Be(publication.Selection);
        payload.Sample.Should().Be(sample);
        payload.LowSampleCount.Should().Be(1);
        payload.TotalEntryCount.Should().Be(4);
        payload.Confidence.Should().Be(TierScopeConfidence.RESOLVED);
        payload.Entries.Select(entry => (entry.Rank, entry.ChampionId, entry.Grade, entry.Games))
            .Should().Equal(includeLowSample
                ? [(1, 2, "S", 200), (2, 1, "S", 200), (3, 4, "B", 8), (4, 3, "C", 120)]
                : [(1, 2, "S", 200), (2, 1, "S", 200), (3, 3, "C", 120)]);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(payload, JsonSerializerOptions.Web));
        var lastRow = json.RootElement.GetProperty("entries").EnumerateArray().Last();
        lastRow.GetProperty("grade").GetString().Should().Be("C");
        lastRow.GetProperty("rank").GetInt32().Should().Be(includeLowSample ? 4 : 3);
        scope.Entries.Select(entry => entry.ChampionId).Should().Equal(4, 2, 1, 3);
        service.Verify(x => x.GetPublishedTierListAsync("TOP", "EMERALD_PLUS", "KR", "aram", publication, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetTierList_RejectsUnsupportedQueue()
    {
        await using var db = CreateDbContext();
        var service = new Mock<IChampionAnalyticsService>();
        var controller = new AnalyticsController(
            service.Object,
            new AnalyticsPatchQueryService(db),
            Options.Create(new MultiRegionIngestionOptions()),
            new AnalyticsPublicationService(db));

        var result = await controller.GetTierList(queue: "normal", ct: CancellationToken.None);

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
