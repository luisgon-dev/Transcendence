namespace Transcendence.Service.Core.Services.Analytics.Models;

/// <summary>
/// Query filter for champion analytics.
/// </summary>
/// <param name="RankTier">ALL, EMERALD_PLUS, Iron, Bronze, Silver, Gold, Platinum, Emerald, Diamond, Master, Grandmaster, Challenger</param>
/// <param name="Region">Optional region filter (default: global)</param>
/// <param name="Role">TOP, JUNGLE, MIDDLE, BOTTOM, UTILITY</param>
/// <param name="Patch">Optional analytics patch version. Defaults to active patch.</param>
/// <param name="QueueFamily">Ranked Solo/Duo, ARAM, Arena, or Ranked Flex. Defaults to Ranked Solo/Duo.</param>
public record ChampionAnalyticsFilter(
    string? RankTier = null,
    string? Region = null,
    string? Role = null,
    string? Patch = null,
    string? QueueFamily = null
);
