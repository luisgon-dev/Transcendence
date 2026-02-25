namespace Transcendence.Service.Core.Services.RiotApi;

public static class QueueCatalog
{
    public const int RankedSoloDuoQueueId = 420;
    public const int RankedFlexQueueId = 440;

    public const string QueueFamilyAll = "ALL";
    public const string QueueFamilyRankedSoloDuo = "RANKED_SOLO_DUO";
    public const string QueueFamilyRankedFlex = "RANKED_FLEX";
    public const string QueueFamilyNormalSummonersRift = "NORMAL_SR";
    public const string QueueFamilyAram = "ARAM";
    public const string QueueFamilyClash = "CLASH";
    public const string QueueFamilyArena = "ARENA";
    public const string QueueFamilyRotating = "ROTATING";
    public const string QueueFamilyBot = "BOT";
    public const string QueueFamilyCustom = "CUSTOM";
    public const string QueueFamilyOther = "OTHER";

    private static readonly HashSet<int> AramQueueIds = [450];
    private static readonly HashSet<int> NormalSummonersRiftQueueIds = [400, 430, 490];
    private static readonly HashSet<int> ClashQueueIds = [700];
    private static readonly HashSet<int> ArenaQueueIds = [1700, 1710, 1810, 1820, 1830, 1840];
    private static readonly HashSet<int> BotQueueIds = [820, 830, 840, 850];
    private static readonly HashSet<int> CustomQueueIds = [0];
    private static readonly HashSet<int> RotatingQueueIds =
    [
        76, 78, 83, 98, 100, 310, 313, 315, 317, 318, 325, 600, 610, 720, 900, 920, 940, 1020, 1300, 1400, 1900
    ];

    private static readonly HashSet<int> ExcludedQueueIds = [..CustomQueueIds, ..BotQueueIds];
    private static readonly string[] KnownFamilies =
    [
        QueueFamilyAll,
        QueueFamilyRankedSoloDuo,
        QueueFamilyRankedFlex,
        QueueFamilyNormalSummonersRift,
        QueueFamilyAram,
        QueueFamilyClash,
        QueueFamilyArena,
        QueueFamilyRotating,
        QueueFamilyBot,
        QueueFamilyCustom,
        QueueFamilyOther
    ];

    public static bool IsRankedAnalyticsQueue(int queueId)
    {
        return queueId == RankedSoloDuoQueueId;
    }

    public static bool IsInDefaultHistoryScope(int queueId)
    {
        return !ExcludedQueueIds.Contains(queueId);
    }

    public static IReadOnlyList<string> GetKnownQueueFamilies()
    {
        return KnownFamilies;
    }

    public static int ParseQueueId(string? queueTypeOrId)
    {
        return int.TryParse(queueTypeOrId, out var queueId) ? queueId : 0;
    }

    public static string ResolveQueueFamily(int queueId)
    {
        if (queueId == RankedSoloDuoQueueId) return QueueFamilyRankedSoloDuo;
        if (queueId == RankedFlexQueueId) return QueueFamilyRankedFlex;
        if (AramQueueIds.Contains(queueId)) return QueueFamilyAram;
        if (NormalSummonersRiftQueueIds.Contains(queueId)) return QueueFamilyNormalSummonersRift;
        if (ClashQueueIds.Contains(queueId)) return QueueFamilyClash;
        if (ArenaQueueIds.Contains(queueId)) return QueueFamilyArena;
        if (RotatingQueueIds.Contains(queueId)) return QueueFamilyRotating;
        if (BotQueueIds.Contains(queueId)) return QueueFamilyBot;
        if (CustomQueueIds.Contains(queueId)) return QueueFamilyCustom;
        return QueueFamilyOther;
    }

    public static string ResolveQueueLabel(int queueId)
    {
        return queueId switch
        {
            RankedSoloDuoQueueId => "Ranked Solo/Duo",
            RankedFlexQueueId => "Ranked Flex",
            400 => "Normal Draft",
            430 => "Normal Blind",
            490 => "Quickplay",
            450 => "ARAM",
            700 => "Clash",
            1700 => "Arena",
            _ => queueId.ToString()
        };
    }
}
