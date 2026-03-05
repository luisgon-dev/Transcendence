using FluentAssertions;
using Microsoft.Extensions.Options;
using Transcendence.Service.Core.Services.Jobs.Configuration;
using Transcendence.Service.Core.Services.Jobs.Priority;

namespace Transcendence.Service.Core.Tests.Jobs;

public class PriorityScoringPolicyTests
{
    [Fact]
    public void RankCandidates_PatchRelevantCandidatesAreRankedAheadOfFreshNonPatchCandidates()
    {
        var now = new DateTime(2026, 3, 5, 18, 0, 0, DateTimeKind.Utc);
        var patchRelease = now.AddHours(-2);
        var candidates = new[]
        {
            new TestCandidate("na1:patch-stale:one", now.AddHours(-6), false),
            new TestCandidate("na1:fresh:two", now.AddMinutes(-10), false)
        };

        var ranked = Rank(candidates, patchRelease, now);

        ranked.Select(candidate => candidate.CanonicalIdentity)
            .Should()
            .Equal("na1:patch-stale:one", "na1:fresh:two");
    }

    [Fact]
    public void RankCandidates_WhenPatchRelevanceMatches_StalerCandidateComesFirst()
    {
        var now = new DateTime(2026, 3, 5, 18, 0, 0, DateTimeKind.Utc);
        var patchRelease = now.AddHours(-5);
        var candidates = new[]
        {
            new TestCandidate("na1:older:one", now.AddHours(-4), false),
            new TestCandidate("na1:newer:two", now.AddHours(-1), false)
        };

        var ranked = Rank(candidates, patchRelease, now);

        ranked.Select(candidate => candidate.CanonicalIdentity)
            .Should()
            .Equal("na1:older:one", "na1:newer:two");
    }

    [Fact]
    public void RankCandidates_WhenPatchAndStalenessMatch_FavoriteBiasWins()
    {
        var now = new DateTime(2026, 3, 5, 18, 0, 0, DateTimeKind.Utc);
        var patchRelease = now.AddHours(-5);
        var updatedAt = now.AddHours(-2);
        var candidates = new[]
        {
            new TestCandidate("na1:favorite:one", updatedAt, true),
            new TestCandidate("na1:tracked:two", updatedAt, false)
        };

        var ranked = Rank(candidates, patchRelease, now);

        ranked.Select(candidate => candidate.CanonicalIdentity)
            .Should()
            .Equal("na1:favorite:one", "na1:tracked:two");
    }

    [Fact]
    public void RankCandidates_WhenScoresAreEqual_UsesCanonicalIdentityTieBreak()
    {
        var now = new DateTime(2026, 3, 5, 18, 0, 0, DateTimeKind.Utc);
        var patchRelease = now.AddHours(-5);
        var updatedAt = now.AddHours(-1);
        var candidates = new[]
        {
            new TestCandidate("na1:beta:one", updatedAt, false),
            new TestCandidate("na1:alpha:one", updatedAt, false),
            new TestCandidate("na1:gamma:one", updatedAt, false)
        };

        var ranked = Rank(candidates, patchRelease, now);

        ranked.Select(candidate => candidate.CanonicalIdentity)
            .Should()
            .Equal("na1:alpha:one", "na1:beta:one", "na1:gamma:one");
    }

    [Fact]
    public void RankCandidates_DuplicateCanonicalIdentity_KeepsOldestUpdatedAtDeterministically()
    {
        var now = new DateTime(2026, 3, 5, 18, 0, 0, DateTimeKind.Utc);
        var patchRelease = now.AddHours(1);
        var candidates = new[]
        {
            new TestCandidate("na1:dup:one", now.AddMinutes(-15), false),
            new TestCandidate("na1:dup:one", now.AddHours(-3), false)
        };

        var ranked = Rank(
            candidates,
            patchRelease,
            now,
            new IngestionPriorityPolicyOptions
            {
                PatchRelevanceWeight = 1,
                StalenessWeight = 0,
                FavoriteWeight = 0,
                StalenessSaturationMinutes = 180
            });

        ranked.Should().HaveCount(1);
        ranked[0].UpdatedAtUtc.Should().Be(now.AddHours(-3));
    }

    private static IReadOnlyList<TestCandidate> Rank(
        IEnumerable<TestCandidate> candidates,
        DateTime patchReleaseUtc,
        DateTime evaluationUtc,
        IngestionPriorityPolicyOptions? options = null)
    {
        var policy = new IngestionPriorityScoringPolicy(Options.Create(options ?? new IngestionPriorityPolicyOptions()));
        return policy.RankCandidates(
            candidates,
            candidate => new IngestionPriorityCandidate(
                candidate.CanonicalIdentity,
                candidate.UpdatedAtUtc,
                candidate.IsFavorite),
            new IngestionPriorityContext(patchReleaseUtc, evaluationUtc),
            maxCandidates: 10);
    }

    private sealed record TestCandidate(string CanonicalIdentity, DateTime UpdatedAtUtc, bool IsFavorite);
}
