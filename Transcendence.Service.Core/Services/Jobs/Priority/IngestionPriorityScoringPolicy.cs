using Microsoft.Extensions.Options;
using Transcendence.Service.Core.Services.Jobs.Configuration;

namespace Transcendence.Service.Core.Services.Jobs.Priority;

public class IngestionPriorityScoringPolicy(IOptions<IngestionPriorityPolicyOptions> options)
    : IIngestionPriorityScoringPolicy
{
    public double ComputeScore(IngestionPriorityCandidate candidate, IngestionPriorityContext context)
    {
        var config = options.Value;
        var patchReleaseUtc = EnsureUtc(context.PatchReleaseUtc);
        var evaluationUtc = EnsureUtc(context.EvaluationUtc);
        var updatedAtUtc = EnsureUtc(candidate.UpdatedAtUtc);

        var patchSignal = updatedAtUtc <= patchReleaseUtc ? 1d : 0d;
        var saturationMinutes = Math.Max(1, config.StalenessSaturationMinutes);
        var stalenessMinutes = Math.Max(0d, (evaluationUtc - updatedAtUtc).TotalMinutes);
        var stalenessSignal = Math.Clamp(stalenessMinutes / saturationMinutes, 0d, 1d);
        var favoriteSignal = candidate.IsFavorite ? 1d : 0d;

        return patchSignal * Math.Max(0d, config.PatchRelevanceWeight)
               + stalenessSignal * Math.Max(0d, config.StalenessWeight)
               + favoriteSignal * Math.Max(0d, config.FavoriteWeight);
    }

    public IReadOnlyList<TCandidate> RankCandidates<TCandidate>(
        IEnumerable<TCandidate> candidates,
        Func<TCandidate, IngestionPriorityCandidate> candidateSelector,
        IngestionPriorityContext context,
        int maxCandidates)
    {
        var boundedMax = Math.Max(1, maxCandidates);

        return candidates
            .Select(candidate => BuildRankedCandidate(candidate, candidateSelector(candidate), context))
            .OrderByDescending(ranked => ranked.Score)
            .ThenBy(ranked => ranked.Signal.CanonicalIdentity, StringComparer.Ordinal)
            .ThenBy(ranked => ranked.Signal.UpdatedAtUtc)
            .DistinctBy(ranked => ranked.Signal.CanonicalIdentity, StringComparer.Ordinal)
            .Take(boundedMax)
            .Select(ranked => ranked.Candidate)
            .ToList();
    }

    private RankedCandidate<TCandidate> BuildRankedCandidate<TCandidate>(
        TCandidate candidate,
        IngestionPriorityCandidate signal,
        IngestionPriorityContext context)
    {
        var normalizedIdentity = signal.CanonicalIdentity?.Trim() ?? string.Empty;
        var normalizedSignal = signal with
        {
            CanonicalIdentity = normalizedIdentity,
            UpdatedAtUtc = EnsureUtc(signal.UpdatedAtUtc)
        };
        var score = ComputeScore(normalizedSignal, context);
        return new RankedCandidate<TCandidate>(candidate, normalizedSignal, score);
    }

    private static DateTime EnsureUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private sealed record RankedCandidate<TCandidate>(
        TCandidate Candidate,
        IngestionPriorityCandidate Signal,
        double Score);
}
