using Transcendence.Service.Core.Services.Analytics.Models;

namespace Transcendence.Service.Core.Services.Analytics;

public static class ChampionRoleResolver
{
    private static readonly HashSet<string> LaneRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "TOP", "JUNGLE", "MIDDLE", "BOTTOM", "UTILITY"
    };

    public static string? PickMostPlayed(IEnumerable<ChampionWinRateDto>? winRates)
    {
        if (winRates == null)
            return null;

        return winRates
            .Select(row => new { Role = NormalizeLane(row.Role), row.Games })
            .Where(row => row.Role != null)
            .GroupBy(row => row.Role!)
            .Select(group => new
            {
                Role = group.Key,
                Games = group.Sum(row => Math.Max(0, row.Games))
            })
            .OrderByDescending(row => row.Games)
            .ThenBy(row => row.Role, StringComparer.Ordinal)
            .Select(row => row.Role)
            .FirstOrDefault();
    }

    private static string? NormalizeLane(string? role)
    {
        if (string.IsNullOrWhiteSpace(role))
            return null;

        var normalized = role.Trim().ToUpperInvariant();
        return LaneRoles.Contains(normalized) ? normalized : null;
    }
}
