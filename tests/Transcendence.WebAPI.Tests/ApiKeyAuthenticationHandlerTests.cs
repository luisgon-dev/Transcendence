using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Transcendence.Service.Core.Services.Auth.Interfaces;
using Transcendence.Service.Core.Services.Auth.Models;
using Transcendence.WebAPI.Security;

namespace Transcendence.WebAPI.Tests;

public sealed class ApiKeyAuthenticationHandlerTests
{
    private const string Scheme = "ApiKeyTest";

    [Fact]
    public async Task Missing_header_returns_no_result_without_validation()
    {
        var apiKeys = new Mock<IApiKeyService>();
        var context = BuildContext(apiKeys.Object);

        var result = await context.AuthenticateAsync(Scheme);

        result.None.Should().BeTrue();
        apiKeys.Verify(x => x.ValidateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Empty_header_fails_without_validation()
    {
        var apiKeys = new Mock<IApiKeyService>();
        var context = BuildContext(apiKeys.Object, "   ");

        var result = await context.AuthenticateAsync(Scheme);

        result.Succeeded.Should().BeFalse();
        result.Failure.Should().NotBeNull();
        result.Failure!.Message.Should().Be("API key header is empty.");
        apiKeys.Verify(x => x.ValidateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Invalid_key_fails_authentication()
    {
        var apiKeys = new Mock<IApiKeyService>();
        apiKeys.Setup(x => x.ValidateAsync("invalid", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ApiKeyValidationResult?)null);
        var context = BuildContext(apiKeys.Object, "invalid");

        var result = await context.AuthenticateAsync(Scheme);

        result.Succeeded.Should().BeFalse();
        result.Failure!.Message.Should().Be("Invalid API key.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Valid_key_creates_app_identity_and_optional_bootstrap_claim(bool isBootstrap)
    {
        var id = Guid.NewGuid();
        var apiKeys = new Mock<IApiKeyService>();
        apiKeys.Setup(x => x.ValidateAsync("valid", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiKeyValidationResult(id, "test-client", isBootstrap));
        var context = BuildContext(apiKeys.Object, "valid");

        var result = await context.AuthenticateAsync(Scheme);

        result.Succeeded.Should().BeTrue();
        var principal = result.Principal!;
        principal.FindFirstValue(ClaimTypes.NameIdentifier).Should().Be(id.ToString());
        principal.Identity!.Name.Should().Be("test-client");
        principal.IsInRole("app").Should().BeTrue();
        principal.HasClaim("bootstrap", "true").Should().Be(isBootstrap);
        result.Ticket!.AuthenticationScheme.Should().Be(Scheme);
    }

    private static DefaultHttpContext BuildContext(IApiKeyService apiKeyService, string? header = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(apiKeyService);
        services.AddAuthentication(Scheme)
            .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(Scheme, _ => { });

        var context = new DefaultHttpContext
        {
            RequestServices = services.BuildServiceProvider()
        };
        if (header != null)
            context.Request.Headers["X-API-Key"] = header;
        return context;
    }
}
