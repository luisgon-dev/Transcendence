using Camille.Enums;

namespace Transcendence.Service.Core.Services.RiotApi;

public static class RiotRoutingExtensions
{
    /// <summary>
    /// Regional route to use for Account-V1 / Riot-ID lookups.
    /// Account-V1 is a global service that is only served from the AMERICAS / ASIA / EUROPE
    /// clusters — it has <b>no SEA cluster</b>. Camille maps OCE (<see cref="PlatformRoute.OC1"/>)
    /// and other South-East-Asia platforms to <see cref="RegionalRoute.SEA"/>, which is correct for
    /// Match-V5 but 404s for Account-V1. Account-V1 resolves any PUUID/Riot-ID from any cluster, so
    /// we fall those platforms back to <see cref="RegionalRoute.ASIA"/> (the nearest valid cluster).
    /// Match-V5 routing must keep using <c>ToRegional()</c> directly so it stays on SEA.
    /// </summary>
    public static RegionalRoute ToAccountRegional(this PlatformRoute platform)
    {
        var regional = platform.ToRegional();
        return regional == RegionalRoute.SEA ? RegionalRoute.ASIA : regional;
    }
}
