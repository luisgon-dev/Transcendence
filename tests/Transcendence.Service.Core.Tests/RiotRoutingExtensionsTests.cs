using Camille.Enums;
using FluentAssertions;
using Transcendence.Service.Core.Services.RiotApi;

namespace Transcendence.Service.Core.Tests;

/// <summary>
/// Account-V1 routing: Camille maps South-East-Asia platforms (OCE + the SEA servers) to
/// <see cref="RegionalRoute.SEA"/>, which is correct for Match-V5 but 404s for Account-V1 (which has
/// no SEA cluster). <see cref="RiotRoutingExtensions.ToAccountRegional"/> clamps SEA -> ASIA. This
/// was a real prod bug — every OC1 summoner enrichment 404'd, yielding 0 seeds for the region.
/// Philippines and Thailand moved into SG2 in January 2025, so the retired PH2/TH2 routes are
/// intentionally excluded.
/// </summary>
public class RiotRoutingExtensionsTests
{
    [Theory]
    [InlineData(PlatformRoute.OC1)]   // OCE -> SEA (Match-V5) -> must clamp to ASIA for Account-V1
    [InlineData(PlatformRoute.SG2)]
    [InlineData(PlatformRoute.TW2)]
    [InlineData(PlatformRoute.VN2)]
    public void ToAccountRegional_ClampsSeaPlatformsToAsia(PlatformRoute platform)
    {
        // Precondition: these platforms really do route to SEA for Match-V5 (so the clamp matters).
        platform.ToRegional().Should().Be(RegionalRoute.SEA);

        platform.ToAccountRegional().Should().Be(RegionalRoute.ASIA);
    }

    [Theory]
    [InlineData(PlatformRoute.NA1, RegionalRoute.AMERICAS)]
    [InlineData(PlatformRoute.BR1, RegionalRoute.AMERICAS)]
    [InlineData(PlatformRoute.LA1, RegionalRoute.AMERICAS)]
    [InlineData(PlatformRoute.EUW1, RegionalRoute.EUROPE)]
    [InlineData(PlatformRoute.EUN1, RegionalRoute.EUROPE)]
    [InlineData(PlatformRoute.TR1, RegionalRoute.EUROPE)]
    [InlineData(PlatformRoute.KR, RegionalRoute.ASIA)]
    [InlineData(PlatformRoute.JP1, RegionalRoute.ASIA)]
    public void ToAccountRegional_LeavesNonSeaPlatformsOnTheirRegionalCluster(
        PlatformRoute platform, RegionalRoute expected)
    {
        // Non-SEA platforms are unaffected — Account-V1 routing == Match-V5 routing for them.
        platform.ToAccountRegional().Should().Be(expected);
        platform.ToAccountRegional().Should().Be(platform.ToRegional());
    }
}
