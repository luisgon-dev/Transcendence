using FluentAssertions;
using Transcendence.Service.Core.Services.Auth.Implementations;

namespace Transcendence.Service.Core.Tests;

public class ApiKeyServiceTests
{
    [Theory]
    [InlineData("bootstrap-secret", "bootstrap-secret")]
    [InlineData(" bootstrap-secret ", "bootstrap-secret")]
    public void BootstrapKeysMatch_AcceptsEquivalentConfiguredKey(string configured, string candidate)
    {
        ApiKeyService.BootstrapKeysMatch(configured, candidate).Should().BeTrue();
    }

    [Theory]
    [InlineData("bootstrap-secret", "bootstrap-secreu")]
    [InlineData("bootstrap-secret", "short")]
    [InlineData("bootstrap-secret", "")]
    public void BootstrapKeysMatch_RejectsDifferentCandidate(string configured, string candidate)
    {
        ApiKeyService.BootstrapKeysMatch(configured, candidate).Should().BeFalse();
    }
}
