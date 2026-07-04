namespace Transcendence.Service.Core.Services.Jobs;

public sealed record RankedCountClassification(bool CountsTowardRankedTotal, string? ExclusionReason);

public static class RankedMatchCountClassifier
{
    public const int Version = 1;

    public static RankedCountClassification Classify(
        int queueId,
        string? endOfGameResult,
        bool? gameEndedInEarlySurrender)
    {
        if (queueId != Transcendence.Service.Core.Services.RiotApi.QueueCatalog.RankedSoloDuoQueueId)
            return new RankedCountClassification(false, "NON_RANKED_SOLO_DUO");

        if (!string.Equals(endOfGameResult, "GameComplete", StringComparison.OrdinalIgnoreCase))
            return new RankedCountClassification(false, $"END_OF_GAME_RESULT:{endOfGameResult ?? "UNKNOWN"}");

        if (gameEndedInEarlySurrender == true)
            return new RankedCountClassification(false, "EARLY_SURRENDER_OR_REMAKE");

        return new RankedCountClassification(true, null);
    }
}
