using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Transcendence.Data;
using Transcendence.Data.Models.LoL.Analytics;
using Transcendence.Service.Core.Services.Analytics.Implementations;
using Transcendence.Service.Core.Services.Analytics.Models;
using Transcendence.Service.Core.Services.Diagnostics;
using Transcendence.Service.Core.Tests.Support;

namespace Transcendence.Service.Core.Tests;

[Collection(BuildLabTelemetryCollection.Name)]
public sealed class BuildLabGenerationCoordinatorTests
{
    private const int Champion = 103;
    private const string Role = "MIDDLE";
    private const string Patch = "16.14";
    private const string ManifestJson = "{\"model\":\"wpa-1\",\"features\":42}";
    private static readonly DateTime Anchor = new(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public async Task PromoteCandidateAsync_RefusesLoweredThresholdsEvenUnderANewDatasetVersion()
    {
        await using var harness = await BuildLabHarness.CreateAsync();
        var candidate = Candidate();
        harness.Db.BuildLabGenerations.Add(candidate);
        harness.Db.AdjustedActionEstimates.Add(PassingEstimate(candidate.Id));
        await harness.Db.SaveChangesAsync();
        // The old bypass keyed the floor on DatasetVersion, which is bound from the same options section
        // as the thresholds, so relabelling the dataset unlocked every gate.
        using var telemetry = new BuildLabTelemetry();
        var coordinator = Coordinator(harness, telemetry, new BuildLabModelingOptions
        {
            Enabled = true,
            DatasetVersion = "build-lab-v2",
            MinimumObservedActions = 10,
            MinimumEffectiveSampleSize = 5
        });

        var promoted = await coordinator.PromoteCandidateAsync(candidate.Id, actor: "operator");

        promoted.Should().BeFalse();
        var reloaded = await Reload(harness, candidate.Id);
        reloaded.Status.Should().Be(BuildLabGenerationStatus.Failed);
        reloaded.IsActive.Should().BeFalse();
        reloaded.FailureReason.Should().Contain("v1 floor");
    }

    [Fact]
    public async Task PromoteCandidateAsync_LeavesExactlyOneActiveGenerationAndRetiresThePrevious()
    {
        await using var harness = await BuildLabHarness.CreateAsync();
        var previous = Candidate();
        previous.Status = BuildLabGenerationStatus.Ready;
        previous.IsActive = true;
        previous.PromotedAtUtc = Anchor.AddDays(-1);
        var candidate = Candidate();
        harness.Db.BuildLabGenerations.AddRange(previous, candidate);
        harness.Db.AdjustedActionEstimates.Add(PassingEstimate(candidate.Id));
        await harness.Db.SaveChangesAsync();
        using var telemetry = new BuildLabTelemetry();
        var coordinator = Coordinator(harness, telemetry);

        var promoted = await coordinator.PromoteCandidateAsync(candidate.Id, actor: "operator");

        promoted.Should().BeTrue();
        await using var verification = harness.NewContext();
        var rows = await verification.BuildLabGenerations.AsNoTracking().ToListAsync();
        rows.Count(row => row.IsActive).Should().Be(1);
        var active = rows.Single(row => row.IsActive);
        active.Id.Should().Be(candidate.Id);
        active.Status.Should().Be(BuildLabGenerationStatus.Ready);
        active.PromotedAtUtc.Should().NotBeNull();
        var retired = rows.Single(row => row.Id == previous.Id);
        retired.IsActive.Should().BeFalse();
        retired.Status.Should().Be(BuildLabGenerationStatus.Retired);
        retired.RetiredAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task PromoteCandidateAsync_PromotesWhenThePatchHoldoutWasNotTestable()
    {
        // A cohort covering one patch cannot be split across a patch boundary, so HeldOutPatchPassed is
        // false for want of a test. Treating that as a failure blocked every generation on prod, where
        // only one patch had timeline and Emerald+ coverage. It is safe to waive because the gate
        // guards borrowing: with one patch in scope no prior-patch row is borrowed at all.
        await using var harness = await BuildLabHarness.CreateAsync();
        var candidate = Candidate();
        candidate.ValidationMetricsJson = JsonSerializer.Serialize(
            PassingMetrics() with { HeldOutPatchPassed = false, HeldOutPatchApplicable = false });
        harness.Db.BuildLabGenerations.Add(candidate);
        harness.Db.AdjustedActionEstimates.Add(PassingEstimate(candidate.Id));
        await harness.Db.SaveChangesAsync();
        using var telemetry = new BuildLabTelemetry();
        var coordinator = Coordinator(harness, telemetry);

        var promoted = await coordinator.PromoteCandidateAsync(candidate.Id, actor: "operator");

        promoted.Should().BeTrue();
        var reloaded = await Reload(harness, candidate.Id);
        reloaded.Status.Should().Be(BuildLabGenerationStatus.Ready);
        reloaded.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task PromoteCandidateAsync_StillRejectsAPatchHoldoutThatWasTestedAndFailed()
    {
        // The waiver must be narrow: a cohort that COULD be split across patches and then failed the
        // comparison is a real failure, not a missing test.
        await using var harness = await BuildLabHarness.CreateAsync();
        var candidate = Candidate();
        candidate.ValidationMetricsJson = JsonSerializer.Serialize(
            PassingMetrics() with { HeldOutPatchPassed = false, HeldOutPatchApplicable = true });
        harness.Db.BuildLabGenerations.Add(candidate);
        harness.Db.AdjustedActionEstimates.Add(PassingEstimate(candidate.Id));
        await harness.Db.SaveChangesAsync();
        using var telemetry = new BuildLabTelemetry();
        var coordinator = Coordinator(harness, telemetry);

        var promoted = await coordinator.PromoteCandidateAsync(candidate.Id, actor: "operator");

        promoted.Should().BeFalse();
        var reloaded = await Reload(harness, candidate.Id);
        reloaded.Status.Should().Be(BuildLabGenerationStatus.Failed);
    }

    [Fact]
    public async Task PromoteCandidateAsync_RejectsAFailedPatchHoldoutWhenApplicabilityIsUnreported()
    {
        // An older modeler does not emit the applicability field. Absent it, a false result must keep
        // its old meaning and fail the gate, rather than being waived by a missing value.
        await using var harness = await BuildLabHarness.CreateAsync();
        var document = JsonSerializer.SerializeToNode(
            PassingMetrics() with { HeldOutPatchPassed = false })!.AsObject();
        document.Remove("HeldOutPatchApplicable");
        var candidate = Candidate();
        candidate.ValidationMetricsJson = document.ToJsonString();
        harness.Db.BuildLabGenerations.Add(candidate);
        harness.Db.AdjustedActionEstimates.Add(PassingEstimate(candidate.Id));
        await harness.Db.SaveChangesAsync();
        using var telemetry = new BuildLabTelemetry();
        var coordinator = Coordinator(harness, telemetry);

        var promoted = await coordinator.PromoteCandidateAsync(candidate.Id, actor: "operator");

        promoted.Should().BeFalse();
        var reloaded = await Reload(harness, candidate.Id);
        reloaded.Status.Should().Be(BuildLabGenerationStatus.Failed);
    }

    [Theory]
    [InlineData("overall-ece")]
    [InlineData("time-band-ece")]
    [InlineData("brier")]
    [InlineData("log-loss")]
    [InlineData("held-out-patch")]
    [InlineData("leakage")]
    public async Task PromoteCandidateAsync_RejectsACandidateThatFailsAnyModelGate(string failingGate)
    {
        await using var harness = await BuildLabHarness.CreateAsync();
        var metrics = failingGate switch
        {
            "overall-ece" => PassingMetrics() with { OverallEce = 0.9 },
            "time-band-ece" => PassingMetrics() with { MaxTimeBandEce = 0.9 },
            "brier" => PassingMetrics() with { BrierScore = 0.24, BaselineBrierScore = 0.24 },
            "log-loss" => PassingMetrics() with { LogLoss = 0.66, BaselineLogLoss = 0.66 },
            "held-out-patch" => PassingMetrics() with
            {
                HeldOutPatchPassed = false, HeldOutPatchApplicable = true
            },
            "leakage" => PassingMetrics() with { LeakageCheckPassed = false },
            _ => throw new ArgumentOutOfRangeException(nameof(failingGate))
        };
        var candidate = Candidate();
        candidate.ValidationMetricsJson = JsonSerializer.Serialize(metrics);
        harness.Db.BuildLabGenerations.Add(candidate);
        harness.Db.AdjustedActionEstimates.Add(PassingEstimate(candidate.Id));
        await harness.Db.SaveChangesAsync();
        using var telemetry = new BuildLabTelemetry();
        var coordinator = Coordinator(harness, telemetry);

        var promoted = await coordinator.PromoteCandidateAsync(candidate.Id);

        promoted.Should().BeFalse();
        var reloaded = await Reload(harness, candidate.Id);
        reloaded.Status.Should().Be(BuildLabGenerationStatus.Failed);
        reloaded.IsActive.Should().BeFalse();
        reloaded.FailureReason.Should()
            .Be("The candidate model did not pass calibration, baseline, patch, and leakage gates.");
    }

    [Theory]
    [InlineData("{}", "Validation metrics did not report every required calibration, baseline, patch, and leakage field.")]
    [InlineData("null", "Validation metrics are missing or malformed.")]
    [InlineData("{\"overallEce\":", "Validation metrics are missing or malformed.")]
    [InlineData("[]", "Validation metrics are missing or malformed.")]
    public async Task PromoteCandidateAsync_FailsClosedOnMissingOrMalformedMetrics(
        string validationMetricsJson,
        string expectedReason)
    {
        await using var harness = await BuildLabHarness.CreateAsync();
        var candidate = Candidate();
        candidate.ValidationMetricsJson = validationMetricsJson;
        harness.Db.BuildLabGenerations.Add(candidate);
        harness.Db.AdjustedActionEstimates.Add(PassingEstimate(candidate.Id));
        await harness.Db.SaveChangesAsync();
        using var telemetry = new BuildLabTelemetry();
        var coordinator = Coordinator(harness, telemetry);

        var promoted = await coordinator.PromoteCandidateAsync(candidate.Id);

        promoted.Should().BeFalse();
        var reloaded = await Reload(harness, candidate.Id);
        reloaded.Status.Should().Be(BuildLabGenerationStatus.Failed);
        reloaded.FailureReason.Should().Be(expectedReason);
    }

    [Theory]
    [InlineData("OverallEce")]
    [InlineData("MaxTimeBandEce")]
    [InlineData("BrierScore")]
    [InlineData("BaselineBrierScore")]
    [InlineData("LogLoss")]
    [InlineData("BaselineLogLoss")]
    [InlineData("HeldOutPatchPassed")]
    [InlineData("LeakageCheckPassed")]
    public async Task PromoteCandidateAsync_TreatsAnyUnreportedMetricAsAFailedGate(string omittedField)
    {
        await using var harness = await BuildLabHarness.CreateAsync();
        var document = JsonSerializer.SerializeToNode(PassingMetrics())!.AsObject();
        // The modeler omitting a field must not bind to a default that happens to pass.
        document.Remove(omittedField);
        var candidate = Candidate();
        candidate.ValidationMetricsJson = document.ToJsonString();
        harness.Db.BuildLabGenerations.Add(candidate);
        harness.Db.AdjustedActionEstimates.Add(PassingEstimate(candidate.Id));
        await harness.Db.SaveChangesAsync();
        using var telemetry = new BuildLabTelemetry();
        var coordinator = Coordinator(harness, telemetry);

        var promoted = await coordinator.PromoteCandidateAsync(candidate.Id);

        promoted.Should().BeFalse();
        var reloaded = await Reload(harness, candidate.Id);
        reloaded.Status.Should().Be(BuildLabGenerationStatus.Failed);
        reloaded.FailureReason.Should().Be(
            "Validation metrics did not report every required calibration, baseline, patch, and leakage field.");
    }

    [Fact]
    public async Task PromoteReadyCandidatesAsync_FailsAModelingRunNoModelerIsHolding()
    {
        // Liveness is the advisory lock, not a timestamp. Acquiring it proves the modeler's session
        // is gone, however recently the row was touched — the timeout this replaced reaped six
        // consecutive healthy runs whose renewal thread simply could not win the GIL.
        await using var harness = await BuildLabHarness.CreateAsync(modelerHoldsLock: false);
        var abandoned = Candidate();
        abandoned.Status = BuildLabGenerationStatus.Modeling;
        abandoned.LeaseOwner = "modeler-1";
        abandoned.CreatedAtUtc = DateTime.UtcNow;
        harness.Db.BuildLabGenerations.Add(abandoned);
        await harness.Db.SaveChangesAsync();
        using var telemetry = new BuildLabTelemetry();
        var coordinator = Coordinator(harness, telemetry, new BuildLabModelingOptions { Enabled = true });

        var promoted = await coordinator.PromoteReadyCandidatesAsync();

        promoted.Should().Be(0);
        var reaped = await Reload(harness, abandoned.Id);
        reaped.Status.Should().Be(BuildLabGenerationStatus.Failed);
        reaped.FailureReason.Should().Contain("modeler-1");
        reaped.LeaseOwner.Should().BeNull();
    }

    [Fact]
    public async Task PromoteReadyCandidatesAsync_SparesARunWhoseModelerStillHoldsTheLock()
    {
        // The case the old timeout got wrong: a long, healthy run must survive regardless of how
        // long it has been going, because its session is demonstrably still alive.
        await using var harness = await BuildLabHarness.CreateAsync(modelerHoldsLock: true);
        var running = Candidate();
        running.Status = BuildLabGenerationStatus.Modeling;
        running.LeaseOwner = "modeler-2";
        running.CreatedAtUtc = DateTime.UtcNow.AddHours(-6);
        harness.Db.BuildLabGenerations.Add(running);
        await harness.Db.SaveChangesAsync();
        using var telemetry = new BuildLabTelemetry();
        var coordinator = Coordinator(harness, telemetry, new BuildLabModelingOptions { Enabled = true });

        await coordinator.PromoteReadyCandidatesAsync();

        var untouched = await Reload(harness, running.Id);
        untouched.Status.Should().Be(BuildLabGenerationStatus.Modeling);
        untouched.FailureReason.Should().BeNull();
        untouched.LeaseOwner.Should().Be("modeler-2");
    }

    [Fact]
    public async Task PromoteReadyCandidatesAsync_WhenDisabled_NeitherReapsNorPromotes()
    {
        await using var harness = await BuildLabHarness.CreateAsync();
        var now = DateTime.UtcNow;
        // Prod has been observed with the recurring-schedule flag true while Analytics:BuildLab:Enabled
        // was false, so the recurring path must be inert on its own rather than trusting the schedule.
        var abandoned = Candidate();
        abandoned.Status = BuildLabGenerationStatus.Modeling;
        abandoned.LeaseOwner = "modeler-1";
        abandoned.CreatedAtUtc = now.AddHours(-4);
        var candidate = Candidate();
        harness.Db.BuildLabGenerations.AddRange(abandoned, candidate);
        harness.Db.AdjustedActionEstimates.Add(PassingEstimate(candidate.Id));
        await harness.Db.SaveChangesAsync();
        using var telemetry = new BuildLabTelemetry();
        var coordinator = Coordinator(harness, telemetry, new BuildLabModelingOptions
        { Enabled = false });

        var promoted = await coordinator.PromoteReadyCandidatesAsync();

        promoted.Should().Be(0);
        var untouchedLease = await Reload(harness, abandoned.Id);
        untouchedLease.Status.Should().Be(BuildLabGenerationStatus.Modeling);
        untouchedLease.LeaseOwner.Should().Be("modeler-1");
        var untouchedCandidate = await Reload(harness, candidate.Id);
        untouchedCandidate.Status.Should().Be(BuildLabGenerationStatus.Candidate);
        untouchedCandidate.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task PromoteReadyCandidatesAsync_ContinuesPastOneCandidatesFailure()
    {
        // The harness deliberately withholds the SQLite shim for the PostgreSQL advisory-lock statement
        // the pointer flip issues, so the first candidate throws inside its promotion. That is exactly
        // the shape of per-candidate fault the loop has to absorb: the job has no automatic retry, so a
        // poisoned candidate must not strand the rest of the queue until the next tick.
        await using var harness = await BuildLabHarness.CreateAsync(withPromotionLockShim: false);
        var poisoned = Candidate();
        poisoned.CompletedAtUtc = Anchor.AddHours(-2);
        var rejected = Candidate();
        rejected.CompletedAtUtc = Anchor.AddHours(-1);
        rejected.ValidationMetricsJson = JsonSerializer.Serialize(PassingMetrics() with { OverallEce = 0.9 });
        harness.Db.BuildLabGenerations.AddRange(poisoned, rejected);
        harness.Db.AdjustedActionEstimates.AddRange(
            PassingEstimate(poisoned.Id),
            PassingEstimate(rejected.Id));
        await harness.Db.SaveChangesAsync();
        using var telemetry = new BuildLabTelemetry();
        var coordinator = Coordinator(harness, telemetry);

        var promoted = await coordinator.PromoteReadyCandidatesAsync();

        promoted.Should().Be(0);
        var poisonedRow = await Reload(harness, poisoned.Id);
        poisonedRow.Status.Should().Be(BuildLabGenerationStatus.Candidate);
        poisonedRow.IsActive.Should().BeFalse();
        // Reached and graded despite the earlier candidate blowing up mid-promotion.
        var rejectedRow = await Reload(harness, rejected.Id);
        rejectedRow.Status.Should().Be(BuildLabGenerationStatus.Failed);
        rejectedRow.FailureReason.Should()
            .Be("The candidate model did not pass calibration, baseline, patch, and leakage gates.");
    }

    [Fact]
    public async Task Retention_KeepsTheNewestPromotedGenerationsWithNullSafeOrdering()
    {
        await using var harness = await BuildLabHarness.CreateAsync();
        var now = DateTime.UtcNow;
        var oldestPromoted = Retired(promotedAtUtc: now.AddHours(-10), createdAtUtc: now.AddHours(-11), retiredAtUtc: now.AddHours(-2));
        var middlePromoted = Retired(promotedAtUtc: now.AddHours(-5), createdAtUtc: now.AddHours(-6), retiredAtUtc: now.AddHours(-2));
        // Never promoted, but the newest row by creation: a DESC ordering that puts NULLs first would
        // hand it a retention slot and delete a real generation instead.
        var neverPromoted = Retired(promotedAtUtc: null, createdAtUtc: now.AddHours(-1), retiredAtUtc: now.AddHours(-2));
        var candidate = Candidate();
        harness.Db.BuildLabGenerations.AddRange(oldestPromoted, middlePromoted, neverPromoted, candidate);
        harness.Db.AdjustedActionEstimates.Add(PassingEstimate(candidate.Id));
        await harness.Db.SaveChangesAsync();
        using var telemetry = new BuildLabTelemetry();
        var coordinator = Coordinator(harness, telemetry, new BuildLabModelingOptions
        {
            Enabled = true,
            RetainedGenerations = 3,
            RetiredGenerationGraceMinutes = 1
        });

        var promoted = await coordinator.PromoteCandidateAsync(candidate.Id);

        promoted.Should().BeTrue();
        await using var verification = harness.NewContext();
        var remaining = await verification.BuildLabGenerations
            .AsNoTracking()
            .Select(generation => generation.Id)
            .ToListAsync();
        remaining.Should().BeEquivalentTo([candidate.Id, middlePromoted.Id, oldestPromoted.Id]);
        remaining.Should().NotContain(neverPromoted.Id);
    }

    [Fact]
    public async Task PromotionHistory_IsAppendOnlyAcrossPromoteAndRollback()
    {
        await using var harness = await BuildLabHarness.CreateAsync();
        var first = Candidate();
        first.CompletedAtUtc = Anchor.AddHours(-2);
        var second = Candidate();
        second.CompletedAtUtc = Anchor.AddHours(-1);
        harness.Db.BuildLabGenerations.AddRange(first, second);
        harness.Db.AdjustedActionEstimates.AddRange(PassingEstimate(first.Id), PassingEstimate(second.Id));
        await harness.Db.SaveChangesAsync();
        using var telemetry = new BuildLabTelemetry();

        (await Coordinator(harness, telemetry).PromoteCandidateAsync(first.Id, actor: "operator-a"))
            .Should().BeTrue();
        var afterPromotion = History(await Reload(harness, first.Id));
        afterPromotion.Should().ContainSingle().Which.Action.Should().Be("promote");
        var originalPromotion = afterPromotion[0];

        (await Coordinator(harness, telemetry).PromoteCandidateAsync(second.Id, actor: "operator-b"))
            .Should().BeTrue();
        (await Coordinator(harness, telemetry).RollbackAsync(first.Id, actor: "operator-c"))
            .Should().BeTrue();

        var history = History(await Reload(harness, first.Id));
        history.Should().HaveCount(2);
        history[0].Action.Should().Be("promote");
        history[0].Actor.Should().Be("operator-a");
        history[0].AtUtc.Should().Be(originalPromotion.AtUtc);
        history[1].Action.Should().Be("rollback");
        history[1].Actor.Should().Be("operator-c");
        var reloadedFirst = await Reload(harness, first.Id);
        reloadedFirst.IsActive.Should().BeTrue();
        (await Reload(harness, second.Id)).IsActive.Should().BeFalse();
    }

    private static BuildLabGenerationCoordinator Coordinator(
        BuildLabHarness harness,
        BuildLabTelemetry telemetry,
        BuildLabModelingOptions? options = null) =>
        new(
            harness.NewContext(),
            harness.Cache,
            Options.Create(options ?? new BuildLabModelingOptions { Enabled = true }),
            telemetry,
            NullLogger<BuildLabGenerationCoordinator>.Instance);

    private static async Task<BuildLabGeneration> Reload(BuildLabHarness harness, Guid generationId)
    {
        await using var context = harness.NewContext();
        return await context.BuildLabGenerations.AsNoTracking().SingleAsync(row => row.Id == generationId);
    }

    private static IReadOnlyList<BuildLabPromotionHistoryEntry> History(BuildLabGeneration generation) =>
        JsonSerializer.Deserialize<List<BuildLabPromotionHistoryEntry>>(
            generation.PromotionHistoryJson, JsonOptions) ?? [];

    private static BuildLabValidationMetrics PassingMetrics() => new(
        OverallEce: 0.010,
        MaxTimeBandEce: 0.020,
        BrierScore: 0.200,
        BaselineBrierScore: 0.240,
        LogLoss: 0.600,
        BaselineLogLoss: 0.660,
        HeldOutPatchPassed: true,
        LeakageCheckPassed: true);

    private static BuildLabGeneration Candidate() => new()
    {
        Id = Guid.NewGuid(),
        Status = BuildLabGenerationStatus.Candidate,
        IsActive = false,
        Patch = Patch,
        RankScope = "EMERALD_PLUS",
        DatasetVersion = "build-lab-v1",
        StaticDataVersion = Patch,
        ModelVersion = "wpa-1",
        CodeRevision = "rev-1",
        IncludedPatchesJson = JsonSerializer.Serialize(new[] { Patch }),
        IncludedRegionsJson = JsonSerializer.Serialize(new[] { "NA1" }),
        SourceCutoffUtc = Anchor,
        MatchCount = 50_000,
        ArtifactUri = "s3://transcendence/build-lab/wpa-1.tar.zst",
        ArtifactSha256 = Sha256Hex(ManifestJson),
        ArtifactManifestJson = ManifestJson,
        ValidationMetricsJson = JsonSerializer.Serialize(PassingMetrics()),
        PromotionHistoryJson = "[]",
        CreatedAtUtc = Anchor.AddHours(-4),
        CompletedAtUtc = Anchor.AddHours(-1)
    };

    private static BuildLabGeneration Retired(
        DateTime? promotedAtUtc,
        DateTime createdAtUtc,
        DateTime retiredAtUtc)
    {
        var generation = Candidate();
        generation.Status = BuildLabGenerationStatus.Retired;
        generation.PromotedAtUtc = promotedAtUtc;
        generation.CreatedAtUtc = createdAtUtc;
        generation.RetiredAtUtc = retiredAtUtc;
        return generation;
    }

    private static AdjustedActionEstimate PassingEstimate(Guid generationId) => new()
    {
        Id = Guid.NewGuid(),
        GenerationId = generationId,
        Generation = null!,
        ChampionId = Champion,
        Role = Role,
        OpponentChampionId = 0,
        Patch = Patch,
        RegionScope = "GLOBAL",
        DecisionFamily = "ITEM",
        Stage = 1,
        PathPrefixHash = BuildLabService.HashPath([]),
        PathPrefixJson = "[]",
        ActionKey = "3157",
        ActionIdsJson = "[3157]",
        AdjustedWpa = 0.021,
        ConfidenceLow = 0.011,
        ConfidenceHigh = 0.031,
        RawWinRate = 0.53,
        PickRate = 0.24,
        ObservedCount = 4000,
        EffectiveSampleSize = 2000,
        AverageTimingMinutes = 12.5,
        PropensityOverlap = 0.95,
        CovariateBalance = 0.04,
        StableAcrossFolds = true,
        IsPublishable = false,
        EvidenceQuality = "INSUFFICIENT",
        FallbackScope = "NONE",
        BaselineDefinition = "Realistic alternatives at the same stage.",
        ComputedAtUtc = Anchor
    };

    private static string Sha256Hex(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
