using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Moq;
using Transcendence.Service.Core.Services.Analytics.Interfaces;
using Transcendence.Service.Core.Services.Analytics.Models;
using Transcendence.WebAPI.Controllers;

namespace Transcendence.WebAPI.Tests;

public sealed class PublicSavedBuildsControllerTests
{
    private static readonly Guid ShareId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    [Fact]
    public async Task Get_ReturnsNotFound_WhenTheShareTokenIsUnknownOrRevoked()
    {
        var service = new Mock<ISavedBuildService>();
        service
            .Setup(x => x.GetSharedAsync(ShareId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SavedBuildDto?)null);
        var controller = new PublicSavedBuildsController(service.Object);

        var result = await controller.Get(ShareId, CancellationToken.None);

        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task Get_ProjectsTheSharedBuildWithoutEchoingTheShareToken()
    {
        var service = new Mock<ISavedBuildService>();
        service
            .Setup(x => x.GetSharedAsync(ShareId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(SharedBuild());
        var controller = new PublicSavedBuildsController(service.Object);

        var result = await controller.Get(ShareId, CancellationToken.None);

        var payload = result
            .Should().BeOfType<OkObjectResult>().Subject
            .Value.Should().BeOfType<PublicSavedBuildDto>().Subject;
        payload.Name.Should().Be("Lethality rush");
        payload.ChampionId.Should().Be(103);
        payload.Role.Should().Be("MIDDLE");
        payload.Patch.Should().Be("16.14");
        payload.ItemPath.Should().Equal(3006, 3153);
        payload.RuneSelections.Should().Equal(8010);
        payload.Spell1Id.Should().Be(4);
        payload.Spell2Id.Should().Be(14);
        payload.CompatibilityStatus.Should().Be("STALE");
        payload.UnavailableItemIds.Should().Equal(3153);
        payload.AnalyticsChanged.Should().BeTrue();
    }

    /// <summary>
    /// The share token is the only credential guarding this route, so it must not be echoed back in the
    /// anonymous payload and no owner-identifying field may ride along.
    /// </summary>
    [Fact]
    public void PublicPayload_ExposesNeitherTheShareTokenNorOwnerIdentifiers()
    {
        var members = typeof(PublicSavedBuildDto)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .ToList();

        members.Should().NotContain("ShareId");
        members.Should().NotContain("Id");
        members.Should().NotContain("UserAccountId");
        members.Should().NotContain("CreatedAtUtc");
    }

    /// <summary>
    /// This was the API's only anonymous controller shipping without a limiter; an unmetered share-token
    /// lookup lets the 128-bit token space be probed for free. Asserted on the attribute rather than by
    /// exhausting the limiter so the test stays deterministic and wall-clock independent.
    /// </summary>
    [Fact]
    public void ShareLookup_CarriesTheExpensiveReadRateLimitPolicy()
    {
        var limiter = typeof(PublicSavedBuildsController)
            .GetCustomAttribute<EnableRateLimitingAttribute>(inherit: true);

        limiter.Should().NotBeNull();
        limiter!.PolicyName.Should().Be("expensive-read");
        typeof(PublicSavedBuildsController)
            .GetCustomAttribute<DisableRateLimitingAttribute>(inherit: true)
            .Should().BeNull();
    }

    [Fact]
    public void ShareLookup_DeclaresTheThrottledAndMissingBranches()
    {
        var declared = ResponseTypeContractAssertions.DeclaredResponses<PublicSavedBuildsController>(
            nameof(PublicSavedBuildsController.Get));

        declared.Should().Contain(StatusCodes.Status200OK, typeof(PublicSavedBuildDto));
        declared.Should().ContainKey(StatusCodes.Status404NotFound);
        declared.Should().ContainKey(StatusCodes.Status429TooManyRequests);
    }

    private static SavedBuildDto SharedBuild() =>
        new(
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            "Lethality rush",
            103,
            "MIDDLE",
            null,
            "16.14",
            "KR",
            "supported",
            [3006, 3153],
            [8010],
            4,
            14,
            null,
            null,
            AnalyticsChanged: true,
            CompatibilityStatus: "STALE",
            UnavailableItemIds: [3153],
            UnavailableItems: [new SavedBuildUnavailableItemDto(3153, "RETIRED")],
            ShareId: ShareId,
            CreatedAtUtc: new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
            UpdatedAtUtc: new DateTime(2026, 7, 2, 0, 0, 0, DateTimeKind.Utc));
}
