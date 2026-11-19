using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using System.Text.Json;
using Transcendence.Service.Core.Services.Analytics.Interfaces;
using Transcendence.Service.Core.Services.Analytics.Models;
using Transcendence.Service.Core.Services.Jobs.Configuration;
using Transcendence.WebAPI.Controllers;

namespace Transcendence.WebAPI.Tests;

public class AnalyticsPublicationTests
{
    [Theory]
    [InlineData("16.14", AnalyticsPatchSelectionState.Preparing)]
    [InlineData("15.99", AnalyticsPatchSelectionState.Unavailable)]
    public async Task GetProfile_UnpublishedRequestReturnsExactScopeWithoutFanout(
        string requested, AnalyticsPatchSelectionState state)
    {
        var selection = new AnalyticsPatchSelection(state, "16.14", requested, null);
        var publicationService = new Mock<IAnalyticsPublicationService>(MockBehavior.Strict);
        publicationService.Setup(x => x.ResolveAsync("RANKED_FLEX", requested, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AnalyticsPublication(selection));
        var analytics = new Mock<IChampionAnalyticsService>(MockBehavior.Strict);
        var scopes = new Mock<IServiceScopeFactory>(MockBehavior.Strict);
        var controller = new ChampionAnalyticsController(analytics.Object, scopes.Object, publicationService.Object);

        var result = await controller.GetProfile(103, "top", "emerald+", "KR", "flex", requested);

        var payload = result.Result.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<ChampionProfileAnalyticsResponse>().Subject;
        payload.PatchSelection.Should().Be(selection);
        payload.EffectiveRole.Should().Be("TOP");
        payload.QueueFamily.Should().Be("RANKED_FLEX");
        payload.WinRates.Patch.Should().Be(requested);
        payload.WinRates.ByRoleTier.Should().BeEmpty();
        payload.Grade.Should().BeNull();
        payload.Builds.Should().BeEquivalentTo(new ChampionBuildsResponse(
            103, "TOP", "EMERALD_PLUS", "KR", requested, [], [], QueueFamily: "RANKED_FLEX"));
        payload.Matchups.Patch.Should().Be(requested);
        payload.Matchups.AllMatchups.Should().BeEmpty();
        payload.Trend!.Points.Should().BeEmpty();
        analytics.VerifyNoOtherCalls();
        scopes.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetPublishedPatches_ReturnsTypedQueueOptionsWithStringStates()
    {
        var now = DateTime.UtcNow;
        PublishedAnalyticsPatchOptionDto[] options =
        [
            new("16.14", now, now, true, 0, "ARAM", AnalyticsPatchSelectionState.Preparing),
            new("16.13", now.AddDays(-14), now.AddDays(-14), false, 41, "ARAM", AnalyticsPatchSelectionState.Ready)
        ];
        var publication = new Mock<IAnalyticsPublicationService>(MockBehavior.Strict);
        publication.Setup(x => x.GetPatchOptionsAsync("aram", It.IsAny<CancellationToken>())).ReturnsAsync(options);
        var controller = new AnalyticsController(Mock.Of<IChampionAnalyticsService>(MockBehavior.Strict),
            Mock.Of<IAnalyticsPatchQueryService>(MockBehavior.Strict),
            Options.Create(new MultiRegionIngestionOptions()), publication.Object);

        var result = await controller.GetPublishedPatches("aram");

        var payload = result.Should().BeOfType<OkObjectResult>().Subject.Value;
        payload.Should().BeEquivalentTo(options);
        using var json = JsonSerializer.SerializeToDocument(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        json.RootElement[0].GetProperty("state").GetString().Should().Be("Preparing");
        json.RootElement[1].GetProperty("state").GetString().Should().Be("Ready");
    }
}
