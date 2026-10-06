using System.Net;
using FluentAssertions;

namespace Transcendence.IntegrationTests;

/// <summary>
/// The champion profile controller takes optional constructor parameters (a logger and a synergy wait
/// budget for tests). This boots the real host so the controller is activated by its DI container, as
/// in production, and serves the aggregate profile.
/// </summary>
[Collection(PostgresIntegrationCollection.Name)]
public sealed class ChampionProfileHostTests(PostgresIntegrationFixture fixture)
{
    [Fact]
    public async Task ChampionProfile_IsServedByTheRealHost()
    {
        var response = await fixture.Factory.CreateClient()
            .GetAsync("/api/lol/analytics/champions/103/profile?rankTier=EMERALD_PLUS");

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        (await response.Content.ReadAsStringAsync()).Should().Contain("\"championId\":103");
    }
}
