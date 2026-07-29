namespace Transcendence.Service.Core.Services.Analytics.Models;

/// <summary>
/// Aggregated payload for the champion detail page.
/// </summary>
public record ChampionProfileAnalyticsResponse(
    int ChampionId,
    string EffectiveRole,
    ChampionWinRateSummary WinRates,
    ChampionBuildsResponse Builds,
    ChampionMatchupsResponse Matchups,
    ChampionGradeDto? Grade = null,
    string QueueFamily = "RANKED_SOLO_DUO",
    ChampionTrendResponse? Trend = null,
    ChampionSynergiesResponse? Synergies = null,
    ChampionRecommendationSummary? Recommendation = null
);
