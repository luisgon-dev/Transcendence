namespace Transcendence.Service.Core.Services.Jobs.Priority;

public readonly record struct IngestionPriorityCandidate(
    string CanonicalIdentity,
    DateTime UpdatedAtUtc,
    bool IsFavorite);

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
