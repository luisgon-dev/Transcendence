using Camille.Enums;

namespace Transcendence.Service.Core.Services.RiotApi;

public static class PlatformRouteParser
{
    private static readonly IReadOnlyDictionary<string, PlatformRoute> Aliases =
        new Dictionary<string, PlatformRoute>(StringComparer.OrdinalIgnoreCase)
        {
            ["NA"] = PlatformRoute.NA1,
            ["EUW"] = PlatformRoute.EUW1,
            ["EUNE"] = PlatformRoute.EUN1,
            ["KR"] = PlatformRoute.KR,
            ["BR"] = PlatformRoute.BR1,
            ["LAN"] = PlatformRoute.LA1,
            ["LAS"] = PlatformRoute.LA2,
            ["OCE"] = PlatformRoute.OC1,
            ["JP"] = PlatformRoute.JP1,
            ["TR"] = PlatformRoute.TR1
        };

    public static bool TryParse(string? input, out PlatformRoute platformRoute)
    {
        platformRoute = default;
        if (string.IsNullOrWhiteSpace(input))
            return false;

        var normalized = Normalize(input);

        if (Enum.TryParse(normalized, true, out platformRoute))
            return true;

        return Aliases.TryGetValue(normalized, out platformRoute);
    }

    private static string Normalize(string input)
    {
        return input
            .Replace(" ", string.Empty)
            .Replace("-", string.Empty)
            .Replace("_", string.Empty)
            .ToUpperInvariant();
    }
}
