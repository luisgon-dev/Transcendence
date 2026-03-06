using Transcendence.Service.Core.Services.Jobs.Configuration;

namespace Transcendence.Service.Core.Services.Analytics.Models;

public record AnalyticsRegionDto(
    string Code,
    string Label,
    bool IsDefault = false
);

public static class AnalyticsRegionCatalog
{
    public const string GlobalRegionCode = "ALL";

    private static readonly IReadOnlyDictionary<string, string> RegionLabels =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [GlobalRegionCode] = "Global",
            ["NA1"] = "North America",
            ["EUW1"] = "Europe West",
            ["EUN1"] = "Europe Nordic & East",
            ["KR"] = "Korea",
            ["BR1"] = "Brazil",
            ["JP1"] = "Japan",
            ["LA1"] = "LAN",
            ["LA2"] = "LAS",
            ["OC1"] = "Oceania",
            ["RU"] = "Russia",
            ["TR1"] = "Turkey"
        };

    public static string NormalizeOrDefault(string? region)
    {
        if (string.IsNullOrWhiteSpace(region))
            return GlobalRegionCode;

        var normalized = region.Trim().ToUpperInvariant();
        return normalized == GlobalRegionCode ? GlobalRegionCode : normalized;
    }

    public static string? NormalizeToFilter(string? region)
    {
        var normalized = NormalizeOrDefault(region);
        return normalized == GlobalRegionCode ? null : normalized;
    }

    public static IReadOnlyList<AnalyticsRegionDto> BuildAvailableRegions(MultiRegionIngestionOptions options)
    {
        var regions = new List<AnalyticsRegionDto>
        {
            new(GlobalRegionCode, LabelFor(GlobalRegionCode), IsDefault: true)
        };

        foreach (var region in options.Regions.Where(x => x.Enabled))
        {
            var code = NormalizeOrDefault(region.Region);
            if (code == GlobalRegionCode || regions.Any(x => string.Equals(x.Code, code, StringComparison.OrdinalIgnoreCase)))
                continue;

            regions.Add(new AnalyticsRegionDto(code, LabelFor(code)));
        }

        return regions;
    }

    public static bool IsSupported(string? region, IReadOnlyCollection<string> allowedCodes)
    {
        var normalized = NormalizeOrDefault(region);
        return allowedCodes.Contains(normalized, StringComparer.OrdinalIgnoreCase);
    }

    public static string LabelFor(string regionCode)
    {
        return RegionLabels.TryGetValue(regionCode, out var label)
            ? label
            : regionCode.ToUpperInvariant();
    }
}
