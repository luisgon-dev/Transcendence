using Transcendence.Service.Core.Services.RiotApi;

namespace Transcendence.Service.Core.Services.Analytics;

public static class AnalyticsQueueCatalog
{
    public const string AllRoles = "ALL";

    public static readonly IReadOnlyList<string> SupportedQueueFamilies =
    [
        QueueCatalog.QueueFamilyRankedSoloDuo,
        QueueCatalog.QueueFamilyAram,
        QueueCatalog.QueueFamilyArena,
        QueueCatalog.QueueFamilyRankedFlex
    ];

    public static string Normalize(string? value) => value?.Trim().ToUpperInvariant() switch
    {
        "ARAM" => QueueCatalog.QueueFamilyAram,
        "ARENA" => QueueCatalog.QueueFamilyArena,
        "FLEX" or "RANKED_FLEX" or "RANKED_FLEX_SR" => QueueCatalog.QueueFamilyRankedFlex,
        "SOLO" or "SOLO_DUO" or "RANKED_SOLO" or "RANKED_SOLO_DUO" => QueueCatalog.QueueFamilyRankedSoloDuo,
        _ => QueueCatalog.QueueFamilyRankedSoloDuo
    };

    public static bool IsSupported(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return true;
        var token = value.Trim().ToUpperInvariant();
        return token is "ARAM" or "ARENA" or "FLEX" or "RANKED_FLEX" or "RANKED_FLEX_SR"
            or "SOLO" or "SOLO_DUO" or "RANKED_SOLO" or "RANKED_SOLO_DUO";
    }

    public static bool HasRoles(string queueFamily) =>
        queueFamily is QueueCatalog.QueueFamilyRankedSoloDuo or QueueCatalog.QueueFamilyRankedFlex;

    public static string ToQueryToken(string queueFamily) => queueFamily switch
    {
        QueueCatalog.QueueFamilyAram => "aram",
        QueueCatalog.QueueFamilyArena => "arena",
        QueueCatalog.QueueFamilyRankedFlex => "flex",
        _ => "solo"
    };
}
