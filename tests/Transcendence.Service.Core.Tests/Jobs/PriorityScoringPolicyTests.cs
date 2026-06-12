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

    [Fact]
    public void RankCandidates_RecentlyActiveCandidate_RanksAheadOfInactiveAtEqualStaleness()
    {
        var now = new DateTime(2026, 3, 5, 18, 0, 0, DateTimeKind.Utc);
        var patchRelease = now.AddHours(-5);
        var updatedAt = now.AddHours(-3);
        var candidates = new[]
        {
            new TestCandidate("na1:inactive:one", updatedAt, false, now.AddDays(-30)),
            new TestCandidate("na1:active:two", updatedAt, false, now.AddMinutes(-30))
        };

        var ranked = Rank(candidates, patchRelease, now);

        ranked.Select(candidate => candidate.CanonicalIdentity)
            .Should()
            .Equal("na1:active:two", "na1:inactive:one");
    }

    [Fact]
    public void ComputeScore_RecentActivity_IncreasesScoreOverUnknownActivity()
    {
        var now = new DateTime(2026, 3, 5, 18, 0, 0, DateTimeKind.Utc);
        var context = new IngestionPriorityContext(now.AddHours(-5), now);
        var policy = new IngestionPriorityScoringPolicy(Options.Create(new IngestionPriorityPolicyOptions()));
        var updatedAt = now.AddHours(-2);

        var unknownActivity = new IngestionPriorityCandidate("na1:unknown:one", updatedAt, false);
        var recentActivity = new IngestionPriorityCandidate("na1:recent:two", updatedAt, false)
        {
            LastActiveAtUtc = now.AddMinutes(-10)
        };

        // Unknown (null) activity contributes nothing; a recent activity strictly raises the score.
        policy.ComputeScore(recentActivity, context)
            .Should()
            .BeGreaterThan(policy.ComputeScore(unknownActivity, context));
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
                candidate.IsFavorite)
            {
                LastActiveAtUtc = candidate.LastActiveAtUtc
            },
            new IngestionPriorityContext(patchReleaseUtc, evaluationUtc),
            maxCandidates: 10);
    }

    private sealed record TestCandidate(
        string CanonicalIdentity,
        DateTime UpdatedAtUtc,
        bool IsFavorite,
        DateTime? LastActiveAtUtc = null);
}
