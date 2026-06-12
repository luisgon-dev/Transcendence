namespace Transcendence.Service.Core.Services.Jobs.Priority;

public readonly record struct IngestionPriorityCandidate(
    string CanonicalIdentity,
    DateTime UpdatedAtUtc,
    bool IsFavorite)
{
    // Most recent game-creation time across the summoner's ingested matches (Summoner.LastActiveAtUtc).
    // Null when unknown — yields a zero activity signal, so callers that don't supply it are unaffected.
    public DateTime? LastActiveAtUtc { get; init; }
}

public readonly record struct IngestionPriorityContext(
    DateTime PatchReleaseUtc,
    DateTime EvaluationUtc);

public interface IIngestionPriorityScoringPolicy
{
    double ComputeScore(IngestionPriorityCandidate candidate, IngestionPriorityContext context);

    IReadOnlyList<TCandidate> RankCandidates<TCandidate>(
        IEnumerable<TCandidate> candidates,
        Func<TCandidate, IngestionPriorityCandidate> candidateSelector,
        IngestionPriorityContext context,
        int maxCandidates);
}
