using FluentAssertions;
using Microsoft.Extensions.Hosting;
using Moq;
using Transcendence.Service.Core.Services.Auth.Implementations;

namespace Transcendence.Service.Core.Tests;

// Covers the four branches of JwtService.ResolveSigningKey — part of the P1 "auth crypto untested" gap.
public sealed class JwtServiceKeyResolutionTests
{
    private const string DevPlaceholder = "CHANGE_THIS_DEV_ONLY_KEY_32_CHARS_MINIMUM";

    private static IHostEnvironment Env(string name)
    {
        var m = new Mock<IHostEnvironment>();
        m.SetupGet(x => x.EnvironmentName).Returns(name);
        return m.Object;
    }

    [Fact]
    public void Development_without_key_falls_back_to_the_dev_placeholder()
    {
        JwtService.ResolveSigningKey(null, Env("Development"), requireKeyInDevelopment: false)
            .Should().Be(DevPlaceholder);
    }

    [Fact]
    public void Development_with_require_flag_and_no_key_throws()
    {
        var act = () => JwtService.ResolveSigningKey(" ", Env("Development"), requireKeyInDevelopment: true);
        act.Should().Throw<InvalidOperationException>().WithMessage("*Missing Auth:Jwt:Key*");
    }

    [Fact]
    public void Production_without_key_throws()
    {
        var act = () => JwtService.ResolveSigningKey(null, Env("Production"), requireKeyInDevelopment: false);
        act.Should().Throw<InvalidOperationException>().WithMessage("*Missing Auth:Jwt:Key*");
    }

    [Fact]
    public void Production_with_the_dev_placeholder_key_throws()
    {
        var act = () => JwtService.ResolveSigningKey(DevPlaceholder, Env("Production"), requireKeyInDevelopment: false);
        act.Should().Throw<InvalidOperationException>().WithMessage("*development placeholder*");
    }

    [Fact]
    public void A_real_configured_key_is_returned_trimmed()
    {
        JwtService.ResolveSigningKey("  a-real-signing-key-of-sufficient-length-1234  ", Env("Production"), false)
            .Should().Be("a-real-signing-key-of-sufficient-length-1234");
    }
}
