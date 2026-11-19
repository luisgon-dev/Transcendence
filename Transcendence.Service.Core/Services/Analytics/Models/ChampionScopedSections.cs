using System.Globalization;
using System.Text.Json.Serialization;
using Transcendence.Service.Core.Services.Analytics.Implementations;

namespace Transcendence.Service.Core.Services.Analytics.Models;

[JsonConverter(typeof(JsonStringEnumConverter<ChampionSectionState>))]
public enum ChampionSectionState
{
    Ready,
    Preparing,
    Unavailable
}

public sealed record ChampionScopedSection<T>(ChampionSectionState State, DateTime? ComputedAtUtc, T? Data);

public sealed record ChampionScopedSections(
    ChampionScopedSection<ChampionBuildsResponse> Builds,
    ChampionScopedSection<ChampionMatchupsResponse> Matchups);

public sealed record ChampionSectionScope(
    string Patch,
    string QueueFamily,
    string? Region,
    string? RankTier,
    string? Role,
    int ChampionId)
{
    internal string Key => FormattableString.Invariant($"{QueueFamily}:{Region}:{RankTier}:{Role}:{ChampionId}");

    internal ChampionSectionScope? Normalize()
    {
        if (ChampionId <= 0 || Patch.Length is < 3 or > 32 ||
            !AnalyticsQueueCatalog.SupportedQueueFamilies.Contains(QueueFamily))
            return null;

        var parts = Patch.Split('.');
        if (parts.Length != 2 || parts.Any(part => part.Length == 0 || !part.All(char.IsAsciiDigit)))
            return null;

        var region = AnalyticsRegionCatalog.NormalizeOrDefault(Region);
        var rank = AnalyticsScopeMath.ScopeTokenOf(AnalyticsScopeMath.ParseRankTierScope(RankTier));
        var role = AnalyticsQueueCatalog.HasRoles(QueueFamily)
            ? Role?.Trim().ToUpperInvariant()
            : AnalyticsQueueCatalog.AllRoles;
        if (region.Length > 16 || !region.All(char.IsAsciiLetterOrDigit) ||
            !RankTierCatalog.RankScopeTokens.Contains(rank) ||
            role is not ("TOP" or "JUNGLE" or "MIDDLE" or "BOTTOM" or "UTILITY" or AnalyticsQueueCatalog.AllRoles) ||
            (AnalyticsQueueCatalog.HasRoles(QueueFamily) && role == AnalyticsQueueCatalog.AllRoles))
            return null;

        return this with { Region = region, RankTier = rank, Role = role };
    }

    internal static ChampionSectionScope? FromKey(string patch, string key)
    {
        var parts = key.Split(':');
        if (parts.Length != 5 || !int.TryParse(parts[4], NumberStyles.None, CultureInfo.InvariantCulture, out var id))
            return null;

        var normalized = new ChampionSectionScope(patch, parts[0], parts[1], parts[2], parts[3], id).Normalize();
        return normalized?.Key == key ? normalized : null;
    }
}
