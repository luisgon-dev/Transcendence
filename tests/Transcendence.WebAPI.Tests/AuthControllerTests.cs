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
        IPasswordResetService? passwordResetService = null) =>
        new(authService, passwordResetService ?? Mock.Of<IPasswordResetService>());

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
