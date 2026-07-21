using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Transcendence.Service.Core.Services.Auth.Interfaces;
using Transcendence.Service.Core.Services.Auth.Models;
using Transcendence.WebAPI.Controllers;

namespace Transcendence.WebAPI.Tests;

public class AuthControllerTests
{
    private static AuthController CreateController(
        IUserAuthService authService,
        IPasswordResetService? passwordResetService = null,
        IRiotRsoService? riotRsoService = null) =>
        new(
            authService,
            passwordResetService ?? Mock.Of<IPasswordResetService>(),
            riotRsoService ?? Mock.Of<IRiotRsoService>());

    [Fact]
    public void RiotAuthorize_WhenRsoIsUnavailable_ReturnsServiceUnavailable()
    {
        var rso = new Mock<IRiotRsoService>();
        rso.Setup(service => service.CreateAuthorization(It.IsAny<string>()))
            .Throws<RiotRsoUnavailableException>();
        var controller = CreateController(Mock.Of<IUserAuthService>(), riotRsoService: rso.Object);

        var result = controller.RiotAuthorize(new RiotAuthorizationRequest(new string('s', 32)));

        result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(503);
    }

    [Fact]
    public async Task CompleteRiotLogin_WhenExchangeFails_ReturnsUnauthorized()
    {
        var rso = new Mock<IRiotRsoService>();
        rso.Setup(service => service.CompleteLoginAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RiotRsoExchangeException("invalid"));
        var controller = CreateController(Mock.Of<IUserAuthService>(), riotRsoService: rso.Object);

        var result = await controller.CompleteRiotLogin(
            new RiotRsoCompleteRequest("code", "na"),
            CancellationToken.None);

        result.Should().BeOfType<UnauthorizedObjectResult>();
    }

    [Fact]
    public async Task Register_WhenUserAlreadyExists_ReturnsGenericConflictMessage()
    {
        var authService = new Mock<IUserAuthService>();
        authService
            .Setup(x => x.RegisterAsync(It.IsAny<RegisterRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Email is already registered."));

        var controller = CreateController(authService.Object);

        var result = await controller.Register(
            new RegisterRequest("user@example.com", "123456789012"),
            CancellationToken.None);

        var conflict = result.Should().BeOfType<ConflictObjectResult>().Subject;
        conflict.Value.Should().Be("Registration failed.");
    }

    [Fact]
    public async Task Refresh_WhenTokenInvalid_ReturnsUnauthorized()
    {
        var authService = new Mock<IUserAuthService>();
        authService
            .Setup(x => x.RefreshAsync(It.IsAny<RefreshRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AuthTokenResponse?)null);

        var controller = CreateController(authService.Object);

        var result = await controller.Refresh(new RefreshRequest("bad"), CancellationToken.None);

        result.Should().BeOfType<UnauthorizedObjectResult>();
    }

    [Fact]
    public async Task Logout_AlwaysReturnsNoContent()
    {
        var authService = new Mock<IUserAuthService>();
        authService
            .Setup(x => x.LogoutAsync(It.IsAny<RefreshRequest>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var controller = CreateController(authService.Object);

        var result = await controller.Logout(new RefreshRequest("token"), CancellationToken.None);

        result.Should().BeOfType<NoContentResult>();
        authService.Verify(x => x.LogoutAsync(
            It.Is<RefreshRequest>(r => r.RefreshToken == "token"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task InitiatePasswordReset_WhenConfigured_ReturnsGenericAcceptedMessage()
    {
        var resetService = new Mock<IPasswordResetService>();
        resetService.Setup(x => x.InitiateAsync(It.IsAny<PasswordResetRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var controller = CreateController(Mock.Of<IUserAuthService>(), resetService.Object);

        var result = await controller.InitiatePasswordReset(
            new PasswordResetRequest("user@example.com"), CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task InitiatePasswordReset_WhenDeliveryIsDisabled_ReturnsServiceUnavailable()
    {
        var resetService = new Mock<IPasswordResetService>();
        resetService.Setup(x => x.InitiateAsync(It.IsAny<PasswordResetRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var controller = CreateController(Mock.Of<IUserAuthService>(), resetService.Object);

        var result = await controller.InitiatePasswordReset(
            new PasswordResetRequest("user@example.com"), CancellationToken.None);

        result.Should().BeOfType<ObjectResult>()
            .Which.StatusCode.Should().Be(503);
    }

    [Fact]
    public async Task CompletePasswordReset_WithInvalidToken_ReturnsBadRequest()
    {
        var resetService = new Mock<IPasswordResetService>();
        resetService.Setup(x => x.CompleteAsync(It.IsAny<PasswordResetCompleteRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var controller = CreateController(Mock.Of<IUserAuthService>(), resetService.Object);

        var result = await controller.CompletePasswordReset(
            new PasswordResetCompleteRequest("bad", "new-password-123"), CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }
}
