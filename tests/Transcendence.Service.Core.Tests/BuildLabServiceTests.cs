using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Transcendence.Data.Models.LoL.Analytics;
using Transcendence.Service.Core.Services.Analytics.Implementations;
using Transcendence.Service.Core.Services.Analytics.Models;
using Transcendence.Service.Core.Tests.Support;

namespace Transcendence.Service.Core.Tests;

public sealed class BuildLabServiceTests
{
    private const int Champion = 103;
    private const string Role = "MIDDLE";
    private const string Patch = "16.14";
    private const string GatedReason = "Needs at least 1,000 observed actions.";
    private const string PathGatedReason =
        "The complete conditioned path has not passed the sample and interval gates.";
    private static readonly DateTime Anchor = new(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task GetAsync_DisabledShortCircuitsWithoutIssuingAnyDatabaseQuery()
    {
        await using var harness = await BuildLabHarness.CreateAsync();
        harness.Db.BuildLabGenerations.Add(ReadyGeneration());
        await harness.Db.SaveChangesAsync();
        harness.Sql.Clear();
        var service = Service(harness, enabled: false);

        var response = await service.GetAsync(Query());

        response.Available.Should().BeFalse();
        response.UnavailableReason.Should().Be("Adjusted WPA is not enabled on this deployment.");
        response.Provenance.GenerationId.Should().BeNull();
        response.Stages.Should().BeEmpty();
        harness.Sql.Statements.Should().BeEmpty();
    }

    [Fact]
    public async Task GetChampionRecommendationAsync_DisabledShortCircuitsWithoutIssuingAnyDatabaseQuery()
    {
        await using var harness = await BuildLabHarness.CreateAsync();
        harness.Db.BuildLabGenerations.Add(ReadyGeneration());
        await harness.Db.SaveChangesAsync();
        harness.Sql.Clear();
        var service = Service(harness, enabled: false);

        var summary = await service.GetChampionRecommendationAsync(Champion, Role, null, null, null);

        summary.Available.Should().BeFalse();
        summary.UnavailableReason.Should().Be("Adjusted WPA is not enabled on this deployment.");
        harness.Sql.Statements.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAsync_IsUnavailableWhenRowsExistButNoCandidateIsPublishable()
    {
        await using var harness = await BuildLabHarness.CreateAsync();
        var generation = ReadyGeneration();
        harness.Db.BuildLabGenerations.Add(generation);
        harness.Db.AdjustedActionEstimates.Add(Estimate(
            generation.Id, "ITEM", stage: 1, actionKey: "3157", publishable: false));
        await harness.Db.SaveChangesAsync();
        var service = Service(harness);

        var response = await service.GetAsync(Query());

        // The rows are still rendered so the UI can explain itself, but the section must not claim to
        // be available: counting rows instead of publishable candidates rendered a blank section.
        response.Stages.Should().ContainSingle().Which.Candidates.Should().ContainSingle();
        response.Available.Should().BeFalse();
        response.UnavailableReason.Should().Be(GatedReason);
    }

    [Fact]
    public async Task GetAsync_IsUnavailableWhenOnlyANonPublishablePathEstimateExists()
    {
        await using var harness = await BuildLabHarness.CreateAsync();
        var generation = ReadyGeneration();
        harness.Db.BuildLabGenerations.Add(generation);
        harness.Db.AdjustedPathEstimates.Add(PathEstimate(generation.Id, [3157, 3020], publishable: false));
        await harness.Db.SaveChangesAsync();
        var service = Service(harness);

        var response = await service.GetAsync(Query(itemPath: [3157, 3020]));

        response.Stages.Should().BeEmpty();
        response.PathEstimate.Should().NotBeNull();
        response.PathEstimate!.IsPublishable.Should().BeFalse();
        response.Available.Should().BeFalse();
        response.UnavailableReason.Should().Be(PathGatedReason);
    }

    [Fact]
    public async Task GetAsync_WithholdsDescriptiveRatesButDisclosesEvidenceCountsForAGatedCandidate()
    {
        await using var harness = await BuildLabHarness.CreateAsync();
        var generation = ReadyGeneration();
        harness.Db.BuildLabGenerations.Add(generation);
        var gated = Estimate(generation.Id, "ITEM", stage: 1, actionKey: "3157", publishable: false);
        gated.RawWinRate = 0.71;
        gated.PickRate = 0.42;
        gated.ObservedCount = 37;
        gated.EffectiveSampleSize = 21.5;
        harness.Db.AdjustedActionEstimates.Add(gated);
        await harness.Db.SaveChangesAsync();
        var service = Service(harness);

        var response = await service.GetAsync(Query());

        var candidate = response.Stages.Single().Candidates.Single();
        candidate.RawWinRate.Should().BeNull();
        candidate.PickRate.Should().BeNull();
        candidate.AdjustedWpa.Should().BeNull();
        candidate.ConfidenceLow.Should().BeNull();
        candidate.ConfidenceHigh.Should().BeNull();
        candidate.ObservedCount.Should().Be(37);
        candidate.EffectiveSampleSize.Should().Be(21.5);
        candidate.UnavailableReason.Should().Be(GatedReason);
    }

    [Fact]
    public async Task GetAsync_FallsBackToGlobalPerCellRatherThanPerSection()
    {
        await using var harness = await BuildLabHarness.CreateAsync();
        var generation = ReadyGeneration();
        harness.Db.BuildLabGenerations.Add(generation);
        harness.Db.AdjustedActionEstimates.AddRange(
            // Candidate A survives promotion regionally.
            Estimate(generation.Id, "ITEM", 1, "regional-a", regionScope: "NA1", publishable: true),
            // Candidate B was demoted regionally but has a publishable pooled twin.
            Estimate(generation.Id, "ITEM", 1, "twinned-b", regionScope: "NA1", publishable: false),
            Estimate(generation.Id, "ITEM", 1, "twinned-b", regionScope: "GLOBAL", publishable: true));
        await harness.Db.SaveChangesAsync();
        var service = Service(harness);

        var response = await service.GetAsync(Query(region: "NA1"));

        var candidates = response.Stages.Single().Candidates;
        candidates.Select(candidate => candidate.ActionKey)
            .Should().BeEquivalentTo(["regional-a", "twinned-b"]);
        var regional = candidates.Single(candidate => candidate.ActionKey == "regional-a");
        regional.RegionScope.Should().Be("NA1");
        regional.FallbackScope.Should().Be("NONE");
        regional.IsPublishable.Should().BeTrue();
        var pooled = candidates.Single(candidate => candidate.ActionKey == "twinned-b");
        pooled.RegionScope.Should().Be("GLOBAL");
        pooled.FallbackScope.Should().Be("GLOBAL_FALLBACK");
        pooled.IsPublishable.Should().BeTrue();
        response.Available.Should().BeTrue();
    }

    [Fact]
    public async Task GetAsync_ServesABorrowedPatchFromTheActiveGenerationsIncludedSet()
    {
        await using var harness = await BuildLabHarness.CreateAsync();
        var generation = ReadyGeneration(includedPatches: [Patch, "16.13"]);
        harness.Db.BuildLabGenerations.Add(generation);
        harness.Db.AdjustedActionEstimates.Add(Estimate(generation.Id, "ITEM", 1, "3157", publishable: true));
        await harness.Db.SaveChangesAsync();
        var service = Service(harness);

        var response = await service.GetAsync(Query(patch: "16.13"));

        response.Available.Should().BeTrue();
        response.Context.RequestedPatch.Should().Be("16.13");
        response.Context.EffectivePatch.Should().Be(Patch);
        response.Context.RequestedPatch.Should().NotBe(response.Context.EffectivePatch);
    }

    [Fact]
    public async Task GetAsync_RefusesAPatchOutsideTheActiveGenerationsIncludedSet()
    {
        await using var harness = await BuildLabHarness.CreateAsync();
        var generation = ReadyGeneration(includedPatches: [Patch, "16.13"]);
        harness.Db.BuildLabGenerations.Add(generation);
        harness.Db.AdjustedActionEstimates.Add(Estimate(generation.Id, "ITEM", 1, "3157", publishable: true));
        await harness.Db.SaveChangesAsync();
        var service = Service(harness);

        var response = await service.GetAsync(Query(patch: "16.09"));

        response.Available.Should().BeFalse();
        response.UnavailableReason.Should()
            .Be("Patch 16.09 is outside the promoted generation's modeled patch set.");
        response.Stages.Should().BeEmpty();
        response.Context.RequestedPatch.Should().Be("16.09");
        response.Context.EffectivePatch.Should().Be(Patch);
    }

    [Fact]
    public async Task GetAsync_OrdersBootsBeforeTheLegendaryItemSlots()
    {
        await using var harness = await BuildLabHarness.CreateAsync();
        var generation = ReadyGeneration();
        harness.Db.BuildLabGenerations.Add(generation);
        harness.Db.AdjustedActionEstimates.AddRange(
            Estimate(generation.Id, "ITEM", 6, "sixth", publishable: true),
            Estimate(generation.Id, "ITEM", 1, "first", publishable: true),
            Estimate(generation.Id, "BOOTS", 0, "boots", publishable: true),
            Estimate(generation.Id, "FIRST_ITEM_PATH", 0, "path", publishable: true),
            Estimate(generation.Id, "STARTER", 0, "starter", publishable: true));
        await harness.Db.SaveChangesAsync();
        var service = Service(harness);

        var response = await service.GetAsync(Query());

        response.Stages.Select(stage => stage.Family)
            .Should().Equal(["STARTER", "FIRST_ITEM_PATH", "BOOTS", "ITEM", "ITEM"]);
        var bootsIndex = response.Stages.Select((stage, index) => (stage, index))
            .First(entry => entry.stage.Family == "BOOTS").index;
        var lastItemIndex = response.Stages.Select((stage, index) => (stage, index))
            .Last(entry => entry.stage.Family == "ITEM").index;
        bootsIndex.Should().BeLessThan(lastItemIndex);
    }

    [Fact]
    public async Task GetAsync_RejectsAnItemPathOverTheCapInsteadOfTruncatingIt()
    {
        await using var harness = await BuildLabHarness.CreateAsync();
        var service = Service(harness);
        var overlong = Enumerable.Range(1000, 13).ToList();

        var act = () => service.GetAsync(Query(itemPath: overlong));

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("Item path accepts at most 12 ids.*");
        harness.Sql.Statements.Should().BeEmpty();
    }

    [Theory]
    [InlineData("SUPPORT", "ITEMS", "SUPPORTED", null, null, null)]
    [InlineData("MIDDLE", "MASTERIES", "SUPPORTED", null, null, null)]
    [InlineData("MIDDLE", "ITEMS", "OPTIMAL", null, null, null)]
    [InlineData("MIDDLE", "ITEMS", "SUPPORTED", 0, null, null)]
    [InlineData("MIDDLE", "ITEMS", "SUPPORTED", null, "16.14.16.14.16.14.16.14.16.14.16.14", null)]
    [InlineData("MIDDLE", "ITEMS", "SUPPORTED", null, "16.14; DROP TABLE", null)]
    [InlineData("MIDDLE", "ITEMS", "SUPPORTED", null, null, "NA1NA1NA1NA1NA1NA1")]
    public async Task GetAsync_RejectsInvalidQueryContextAsAValidationProblem(
        string role,
        string section,
        string mode,
        int? opponent,
        string? patch,
        string? region)
    {
        await using var harness = await BuildLabHarness.CreateAsync();
        var service = Service(harness);
        var query = new BuildLabQuery(Champion, role, opponent, patch, region, section, mode, [], [], []);

        var act = () => service.GetAsync(query);

        await act.Should().ThrowAsync<ArgumentException>();
        harness.Sql.Statements.Should().BeEmpty();
    }

    [Fact]
    public async Task GetChampionRecommendationAsync_DegradesInvalidContextInsteadOfFailingTheProfile()
    {
        await using var harness = await BuildLabHarness.CreateAsync();
        var service = Service(harness);

        var summary = await service.GetChampionRecommendationAsync(Champion, "SUPPORT", null, null, null);

        summary.Available.Should().BeFalse();
        summary.UnavailableReason.Should().Be("The requested Build Lab context is not valid.");
        summary.FirstItem.Should().BeNull();
        harness.Sql.Statements.Should().BeEmpty();
    }

    [Fact]
    public async Task GetChampionRecommendationAsync_ResolvesTheGenerationOnceForAllThreeSections()
    {
        await using var harness = await BuildLabHarness.CreateAsync();
        var generation = ReadyGeneration();
        harness.Db.BuildLabGenerations.Add(generation);
        harness.Db.AdjustedActionEstimates.AddRange(
            Estimate(generation.Id, "FIRST_ITEM_PATH", 0, "3157", publishable: true),
            Estimate(generation.Id, "RUNE_PAGE", 0, "8112", publishable: true),
            Estimate(generation.Id, "SPELL", 0, "4|14", publishable: true));
        await harness.Db.SaveChangesAsync();
        harness.Sql.Clear();
        var service = Service(harness);

        var summary = await service.GetChampionRecommendationAsync(Champion, Role, null, null, null);

        summary.Available.Should().BeTrue();
        summary.FirstItem!.ActionKey.Should().Be("3157");
        summary.Rune!.ActionKey.Should().Be("8112");
        summary.SpellPair!.ActionKey.Should().Be("4|14");
        summary.Provenance.GenerationId.Should().Be(generation.Id);
        summary.Provenance.DatasetVersion.Should().Be("build-lab-v1");
        // One lookup for all three sections: two reads could straddle a promotion and publish a single
        // provenance block over rows from different generations.
        harness.Sql.CountContaining("\"BuildLabGenerations\"").Should().Be(1);
    }

    private static BuildLabService Service(BuildLabHarness harness, bool enabled = true) =>
        new(harness.Db, harness.Cache, Options.Create(new BuildLabModelingOptions { Enabled = enabled }));

    private static BuildLabQuery Query(
        string section = "ITEMS",
        string mode = "SUPPORTED",
        string? region = null,
        string? patch = null,
        int? opponent = null,
        IReadOnlyList<int>? itemPath = null) =>
        new(Champion, Role, opponent, patch, region, section, mode, itemPath ?? [], [], []);

    private static BuildLabGeneration ReadyGeneration(IReadOnlyList<string>? includedPatches = null) => new()
    {
        Id = Guid.NewGuid(),
        Status = BuildLabGenerationStatus.Ready,
        IsActive = true,
        Patch = Patch,
        RankScope = "EMERALD_PLUS",
        DatasetVersion = "build-lab-v1",
        StaticDataVersion = Patch,
        ModelVersion = "wpa-1",
        CodeRevision = "rev-1",
        IncludedPatchesJson = JsonSerializer.Serialize(includedPatches ?? [Patch]),
        IncludedRegionsJson = JsonSerializer.Serialize(new[] { "NA1", "EUW1" }),
        SourceCutoffUtc = Anchor,
        MatchCount = 50_000,
        CreatedAtUtc = Anchor.AddHours(-4),
        CompletedAtUtc = Anchor.AddHours(-2),
        PromotedAtUtc = Anchor.AddHours(-1)
    };

    private static AdjustedActionEstimate Estimate(
        Guid generationId,
        string family,
        int stage,
        string actionKey,
        bool publishable,
        string regionScope = "GLOBAL",
        IReadOnlyList<int>? pathPrefix = null) => new()
    {
        Id = Guid.NewGuid(),
        GenerationId = generationId,
        Generation = null!,
        ChampionId = Champion,
        Role = Role,
        OpponentChampionId = 0,
        Patch = Patch,
        RegionScope = regionScope,
        DecisionFamily = family,
        Stage = stage,
        PathPrefixHash = BuildLabService.HashPath(pathPrefix ?? []),
        PathPrefixJson = JsonSerializer.Serialize(pathPrefix ?? []),
        ActionKey = actionKey,
        ActionIdsJson = "[3157]",
        AdjustedWpa = 0.021,
        ConfidenceLow = 0.011,
        ConfidenceHigh = 0.031,
        RawWinRate = 0.53,
        PickRate = 0.24,
        ObservedCount = publishable ? 4000 : 40,
        EffectiveSampleSize = publishable ? 2000 : 20,
        AverageTimingMinutes = 12.5,
        PropensityOverlap = 0.95,
        CovariateBalance = 0.04,
        StableAcrossFolds = true,
        IsPublishable = publishable,
        EvidenceQuality = publishable ? "PUBLISHABLE" : "INSUFFICIENT",
        FallbackScope = "NONE",
        BaselineDefinition = "Realistic alternatives at the same stage.",
        UnavailableReason = publishable ? null : GatedReason,
        ComputedAtUtc = Anchor
    };

    private static AdjustedPathEstimate PathEstimate(
        Guid generationId,
        IReadOnlyList<int> itemPath,
        bool publishable,
        string regionScope = "GLOBAL") => new()
    {
        Id = Guid.NewGuid(),
        GenerationId = generationId,
        Generation = null!,
        ChampionId = Champion,
        Role = Role,
        OpponentChampionId = 0,
        Patch = Patch,
        RegionScope = regionScope,
        PathHash = BuildLabService.HashPath(itemPath),
        ItemPathJson = JsonSerializer.Serialize(itemPath),
        EstimatedWinProbability = 0.52,
        AdjustedLift = 0.018,
        ConfidenceLow = 0.008,
        ConfidenceHigh = 0.028,
        ObservedCount = publishable ? 3000 : 30,
        EffectiveSampleSize = publishable ? 1500 : 15,
        IsPublishable = publishable,
        UnavailableReason = publishable ? null : PathGatedReason
    };
}
