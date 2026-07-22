using System.ComponentModel.DataAnnotations;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Transcendence.Service.Core.Services.Auth.Interfaces;
using Transcendence.Service.Core.Services.Auth.Models;
using Transcendence.WebAPI.Controllers;

namespace Transcendence.WebAPI.Tests;

public sealed class ApiContractControllerTests
{
    [Fact]
    public async Task CreateApiKey_ReturnsCreatedWithoutInventingItemLocation()
    {
        var created = new ApiKeyCreateResult(
            Guid.NewGuid(), "automation", "trn_secret", "trn_secr", DateTime.UtcNow, null);
        var apiKeys = new Mock<IApiKeyService>();
        apiKeys
            .Setup(x => x.CreateAsync(It.IsAny<ApiKeyCreateRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(created);
        var audit = new Mock<IAdminAuditService>();
        audit
            .Setup(x => x.WriteAsync(It.IsAny<AdminAuditWriteRequest>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var controller = new ApiKeysController(apiKeys.Object, audit.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        var response = await controller.Create(new ApiKeyCreateRequest("automation"), CancellationToken.None);

        var result = response.Should().BeOfType<ObjectResult>().Subject;
        result.StatusCode.Should().Be(StatusCodes.Status201Created);
        result.Value.Should().BeSameAs(created);
        response.Should().NotBeOfType<CreatedAtActionResult>();
    }

    [Theory]
    [InlineData("", "Tag", "NA1", "GameName")]
    [InlineData("Name", "", "NA1", "TagLine")]
    [InlineData("Name", "Tag", "", "PlatformRegion")]
    public void ProSummonerUpsert_RequiresIdentityFieldsForPostAndPut(
        string gameName,
        string tagLine,
        string platformRegion,
        string expectedMember)
    {
        var request = new UpsertTrackedProSummonerRequest(gameName, tagLine, platformRegion);
        var results = new List<ValidationResult>();

        var valid = Validator.TryValidateObject(request, new ValidationContext(request), results, true);

        valid.Should().BeFalse();
        results.Should().Contain(result => result.MemberNames.Contains(expectedMember));
    }
}
