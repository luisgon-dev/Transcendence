using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Transcendence.Data;
using Transcendence.Data.Models.Auth;
using Transcendence.Data.Models.LoL.Analytics;
using Transcendence.Data.Models.LoL.Static;
using Transcendence.Service.Core.Services.Analytics.Implementations;
using Transcendence.Service.Core.Services.Analytics.Models;
using Transcendence.Service.Core.Tests.Support;

namespace Transcendence.Service.Core.Tests;

public sealed class SavedBuildServiceTests
{
    private const int Champion = 103;
    private const string Role = "MIDDLE";
    private const string Patch = "16.14";
    private const int Available = 3157;
    private const int RemovedFromStore = 3020;
    private const int Retired = 9999;
    private const int Replacement = 3089;
    private const int UnbuyableReplacement = 4001;
    private static readonly DateTime Anchor = new(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Mutations_ByAnotherAccountReportNotFoundAndLeaveTheBuildUntouched()
    {
        await using var harness = await BuildLabHarness.CreateAsync();
        var owner = SeedUser(harness.Db, "owner@example.com");
        var intruder = SeedUser(harness.Db, "intruder@example.com");
        var build = SeedBuild(harness.Db, owner, name: "Owner build", shareId: Guid.NewGuid());
        await harness.Db.SaveChangesAsync();
        var service = Service(harness);

        var updated = await service.UpdateAsync(intruder, build.Id, Request(name: "Hijacked"));
        var shared = await service.ShareAsync(intruder, build.Id);
        var revoked = await service.RevokeShareAsync(intruder, build.Id);
        var deleted = await service.DeleteAsync(intruder, build.Id);

        updated.Should().BeNull();
        shared.Should().BeNull();
        revoked.Should().BeFalse();
        deleted.Should().BeFalse();
        var reloaded = await harness.NewContext().UserSavedBuilds
            .AsNoTracking()
            .SingleAsync(row => row.Id == build.Id);
        reloaded.Name.Should().Be("Owner build");
        reloaded.UserAccountId.Should().Be(owner);
        reloaded.ShareId.Should().Be(build.ShareId);
        reloaded.UpdatedAtUtc.Should().Be(build.UpdatedAtUtc);
    }

    [Fact]
    public async Task RepairAsync_ByAnotherAccountReportsNotFoundAndLeavesTheItemPathUntouched()
    {
        await using var harness = await BuildLabHarness.CreateAsync();
        var owner = SeedUser(harness.Db, "owner@example.com");
        var intruder = SeedUser(harness.Db, "intruder@example.com");
        SeedPatchAndItems(harness.Db);
        var build = SeedBuild(harness.Db, owner, itemPath: [Available, Retired]);
        await harness.Db.SaveChangesAsync();
        var service = Service(harness);

        var repaired = await service.RepairAsync(
            intruder,
            build.Id,
            new SavedBuildRepairRequest([new SavedBuildRepairChoice(Retired, "DROP", null)]));

        repaired.Should().BeNull();
        var reloaded = await harness.NewContext().UserSavedBuilds
            .AsNoTracking()
            .SingleAsync(row => row.Id == build.Id);
        reloaded.ItemPathJson.Should().Be(JsonSerializer.Serialize(new[] { Available, Retired }));
    }

    [Fact]
    public async Task RevokeShareAsync_MakesTheSharedReadReturnNullImmediately()
    {
        await using var harness = await BuildLabHarness.CreateAsync();
        var owner = SeedUser(harness.Db, "owner@example.com");
        var build = SeedBuild(harness.Db, owner);
        await harness.Db.SaveChangesAsync();
        var service = Service(harness);
        var share = await service.ShareAsync(owner, build.Id);
        share.Should().NotBeNull();
        (await service.GetSharedAsync(share!.ShareId)).Should().NotBeNull();

        var revoked = await service.RevokeShareAsync(owner, build.Id);

        revoked.Should().BeTrue();
        (await service.GetSharedAsync(share.ShareId)).Should().BeNull();
    }

    [Fact]
    public async Task CreateAsync_EnforcesThePerUserCap()
    {
        await using var harness = await BuildLabHarness.CreateAsync();
        var owner = SeedUser(harness.Db, "owner@example.com");
        await harness.Db.SaveChangesAsync();
        var service = Service(harness, new SavedBuildOptions { MaximumPerUser = 2 });
        await service.CreateAsync(owner, Request(name: "One"));
        await service.CreateAsync(owner, Request(name: "Two"));

        var act = () => service.CreateAsync(owner, Request(name: "Three"));

        await act.Should().ThrowAsync<SavedBuildLimitExceededException>();
        (await harness.NewContext().UserSavedBuilds.CountAsync()).Should().Be(2);
    }

    [Theory]
    [InlineData("name-too-long")]
    [InlineData("name-blank")]
    [InlineData("patch-too-long")]
    [InlineData("patch-not-numeric")]
    [InlineData("region-too-long")]
    [InlineData("region-not-alphanumeric")]
    [InlineData("item-path-too-long")]
    [InlineData("runes-too-long")]
    [InlineData("role")]
    [InlineData("mode")]
    [InlineData("champion")]
    [InlineData("opponent")]
    public async Task CreateAsync_RejectsOverlongAndMalformedInput(string invalidField)
    {
        await using var harness = await BuildLabHarness.CreateAsync();
        var owner = SeedUser(harness.Db, "owner@example.com");
        await harness.Db.SaveChangesAsync();
        var service = Service(harness);
        var request = invalidField switch
        {
            "name-too-long" => Request(name: new string('n', 121)),
            "name-blank" => Request(name: "   "),
            "patch-too-long" => Request(patch: new string('1', 33)),
            "patch-not-numeric" => Request(patch: "16.14-beta"),
            "region-too-long" => Request(region: new string('n', 17)),
            "region-not-alphanumeric" => Request(region: "na-1"),
            "item-path-too-long" => Request(itemPath: [.. Enumerable.Range(1000, 13)]),
            "runes-too-long" => Request(runes: [.. Enumerable.Range(8000, 13)]),
            "role" => Request(role: "SUPPORT"),
            "mode" => Request(mode: "OPTIMAL"),
            "champion" => Request(championId: 0),
            "opponent" => Request(opponent: 0),
            _ => throw new ArgumentOutOfRangeException(nameof(invalidField))
        };

        var act = () => service.CreateAsync(owner, request);

        await act.Should().ThrowAsync<ArgumentException>();
        (await harness.NewContext().UserSavedBuilds.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ListAsync_ReportsNoSourceGenerationWhenNothingWasPromotedAtSaveTime()
    {
        await using var harness = await BuildLabHarness.CreateAsync();
        var owner = SeedUser(harness.Db, "owner@example.com");
        await harness.Db.SaveChangesAsync();
        var service = Service(harness);
        var created = await service.CreateAsync(owner, Request(itemPath: [Available, RemovedFromStore]));

        created.SourceGenerationId.Should().BeNull();
        created.CompatibilityStatus.Should().Be("NO_SOURCE_GENERATION");
        created.AnalyticsChanged.Should().BeFalse();
        var listed = await service.ListAsync(owner);
        listed.Items.Should().ContainSingle().Which.CompatibilityStatus.Should().Be("NO_SOURCE_GENERATION");
    }

    [Theory]
    // A promotion that did not move the saved setup's own outcome is not a change the user is told about.
    [InlineData(true, 0.020, true, 0.022, false)]
    [InlineData(true, 0.020, true, 0.040, true)]
    [InlineData(true, 0.020, false, 0.020, true)]
    [InlineData(false, null, true, 0.020, true)]
    public async Task AnalyticsChanged_TracksMaterialMovementRatherThanTheGenerationId(
        bool sourcePublishable,
        double? sourceLift,
        bool currentPublishable,
        double currentLift,
        bool expectedChanged)
    {
        await using var harness = await BuildLabHarness.CreateAsync();
        var owner = SeedUser(harness.Db, "owner@example.com");
        var previous = Generation(active: false, status: BuildLabGenerationStatus.Retired);
        var current = Generation(active: true, status: BuildLabGenerationStatus.Ready);
        harness.Db.BuildLabGenerations.AddRange(previous, current);
        IReadOnlyList<int> itemPath = [Available, RemovedFromStore];
        var build = SeedBuild(harness.Db, owner, itemPath: itemPath);
        build.SourceGenerationId = previous.Id;
        build.SourceIsPublishable = sourcePublishable;
        build.SourceAdjustedLift = sourceLift;
        harness.Db.AdjustedPathEstimates.Add(new AdjustedPathEstimate
        {
            Id = Guid.NewGuid(),
            GenerationId = current.Id,
            Generation = null!,
            ChampionId = Champion,
            Role = Role,
            OpponentChampionId = 0,
            Patch = Patch,
            RegionScope = "GLOBAL",
            PathHash = BuildLabService.HashPath(itemPath),
            ItemPathJson = JsonSerializer.Serialize(itemPath),
            EstimatedWinProbability = 0.52,
            AdjustedLift = currentLift,
            ConfidenceLow = currentLift - 0.01,
            ConfidenceHigh = currentLift + 0.01,
            ObservedCount = 4000,
            EffectiveSampleSize = 2000,
            IsPublishable = currentPublishable
        });
        await harness.Db.SaveChangesAsync();
        var service = Service(harness);

        var listed = await service.ListAsync(owner);

        var item = listed.Items.Should().ContainSingle().Subject;
        item.SourceGenerationId.Should().Be(previous.Id);
        item.CurrentGenerationId.Should().Be(current.Id);
        item.AnalyticsChanged.Should().Be(expectedChanged);
    }

    [Fact]
    public async Task RepairAsync_RejectsAChoiceTargetingAnItemThatIsStillAvailable()
    {
        await using var harness = await BuildLabHarness.CreateAsync();
        var owner = SeedUser(harness.Db, "owner@example.com");
        SeedPatchAndItems(harness.Db);
        var build = SeedBuild(harness.Db, owner, itemPath: [Available, RemovedFromStore, Retired]);
        await harness.Db.SaveChangesAsync();
        var service = Service(harness);

        var act = () => service.RepairAsync(
            owner,
            build.Id,
            new SavedBuildRepairRequest([new SavedBuildRepairChoice(Available, "DROP", null)]));

        (await act.Should().ThrowAsync<ArgumentException>())
            .WithMessage($"Item {Available} is not an unavailable selection on this saved build.*");
        var reloaded = await harness.NewContext().UserSavedBuilds.AsNoTracking()
            .SingleAsync(row => row.Id == build.Id);
        reloaded.ItemPathJson.Should()
            .Be(JsonSerializer.Serialize(new[] { Available, RemovedFromStore, Retired }));
    }

    [Fact]
    public async Task RepairAsync_RequiresAnExplicitChoiceAndNeverSubstitutesOnItsOwn()
    {
        await using var harness = await BuildLabHarness.CreateAsync();
        var owner = SeedUser(harness.Db, "owner@example.com");
        SeedPatchAndItems(harness.Db);
        var build = SeedBuild(harness.Db, owner, itemPath: [Available, RemovedFromStore, Retired]);
        await harness.Db.SaveChangesAsync();
        var service = Service(harness);

        var act = () => service.RepairAsync(owner, build.Id, new SavedBuildRepairRequest(null));

        await act.Should().ThrowAsync<ArgumentException>();
        var reloaded = await harness.NewContext().UserSavedBuilds.AsNoTracking()
            .SingleAsync(row => row.Id == build.Id);
        reloaded.ItemPathJson.Should()
            .Be(JsonSerializer.Serialize(new[] { Available, RemovedFromStore, Retired }));
    }

    [Fact]
    public async Task RepairAsync_RejectsAReplacementThatIsNotPurchasableOnTheActivePatch()
    {
        await using var harness = await BuildLabHarness.CreateAsync();
        var owner = SeedUser(harness.Db, "owner@example.com");
        SeedPatchAndItems(harness.Db);
        var build = SeedBuild(harness.Db, owner, itemPath: [Available, RemovedFromStore, Retired]);
        await harness.Db.SaveChangesAsync();
        var service = Service(harness);

        var act = () => service.RepairAsync(
            owner,
            build.Id,
            new SavedBuildRepairRequest(
                [new SavedBuildRepairChoice(RemovedFromStore, "REPLACE", UnbuyableReplacement)]));

        await act.Should().ThrowAsync<ArgumentException>();
        var reloaded = await harness.NewContext().UserSavedBuilds.AsNoTracking()
            .SingleAsync(row => row.Id == build.Id);
        reloaded.ItemPathJson.Should()
            .Be(JsonSerializer.Serialize(new[] { Available, RemovedFromStore, Retired }));
    }

    [Fact]
    public async Task RepairAsync_AppliesOnlyTheChoicesTheUserMade()
    {
        await using var harness = await BuildLabHarness.CreateAsync();
        var owner = SeedUser(harness.Db, "owner@example.com");
        SeedPatchAndItems(harness.Db);
        var build = SeedBuild(harness.Db, owner, itemPath: [Available, RemovedFromStore, Retired]);
        await harness.Db.SaveChangesAsync();
        var service = Service(harness);

        var repaired = await service.RepairAsync(
            owner,
            build.Id,
            new SavedBuildRepairRequest([
                new SavedBuildRepairChoice(RemovedFromStore, "replace", Replacement),
                new SavedBuildRepairChoice(Retired, "drop", null)
            ]));

        repaired.Should().NotBeNull();
        repaired!.ItemPath.Should().Equal([Available, Replacement]);
        repaired.UnavailableItemIds.Should().BeEmpty();
        repaired.Patch.Should().Be(Patch);
    }

    private static SavedBuildService Service(BuildLabHarness harness, SavedBuildOptions? options = null) =>
        new(harness.Db, Options.Create(options ?? new SavedBuildOptions()));

    private static SaveBuildRequest Request(
        string name = "My build",
        int championId = Champion,
        string role = Role,
        int? opponent = null,
        string? patch = null,
        string? region = null,
        string? mode = null,
        IReadOnlyList<int>? itemPath = null,
        IReadOnlyList<int>? runes = null) =>
        new(name, championId, role, opponent, patch, region, mode, itemPath, runes, 4, 14);

    private static Guid SeedUser(TranscendenceContext db, string email)
    {
        var id = Guid.NewGuid();
        db.UserAccounts.Add(new UserAccount
        {
            Id = id,
            Email = email,
            EmailNormalized = email.ToUpperInvariant(),
            PasswordHash = "hash",
            CreatedAtUtc = Anchor,
            UpdatedAtUtc = Anchor
        });
        return id;
    }

    private static UserSavedBuild SeedBuild(
        TranscendenceContext db,
        Guid userId,
        string name = "My build",
        IReadOnlyList<int>? itemPath = null,
        Guid? shareId = null)
    {
        var build = new UserSavedBuild
        {
            Id = Guid.NewGuid(),
            UserAccountId = userId,
            UserAccount = null!,
            Name = name,
            ChampionId = Champion,
            Role = Role,
            OpponentChampionId = null,
            Patch = Patch,
            Region = "GLOBAL",
            RankingMode = "SUPPORTED",
            ItemPathJson = JsonSerializer.Serialize(itemPath ?? [Available]),
            RuneSelectionsJson = "[8112]",
            Spell1Id = 4,
            Spell2Id = 14,
            ShareId = shareId,
            CreatedAtUtc = Anchor,
            UpdatedAtUtc = Anchor
        };
        db.UserSavedBuilds.Add(build);
        return build;
    }

    private static BuildLabGeneration Generation(bool active, BuildLabGenerationStatus status) => new()
    {
        Id = Guid.NewGuid(),
        Status = status,
        IsActive = active,
        Patch = Patch,
        RankScope = "EMERALD_PLUS",
        DatasetVersion = "build-lab-v1",
        StaticDataVersion = Patch,
        ModelVersion = "wpa-1",
        CodeRevision = "rev-1",
        IncludedPatchesJson = JsonSerializer.Serialize(new[] { Patch }),
        IncludedRegionsJson = "[]",
        SourceCutoffUtc = Anchor,
        MatchCount = 50_000,
        CreatedAtUtc = Anchor.AddHours(-4),
        CompletedAtUtc = Anchor.AddHours(-2),
        PromotedAtUtc = Anchor.AddHours(-1)
    };

    private static void SeedPatchAndItems(TranscendenceContext db)
    {
        db.Patches.Add(new Patch { Version = Patch, ReleaseDate = Anchor, IsActive = true });
        db.ItemVersions.AddRange(
            Item(Available, "Rod of Ages", inStore: true),
            Item(RemovedFromStore, "Seeker's Armguard", inStore: false),
            Item(Replacement, "Rabadon's Deathcap", inStore: true),
            Item(UnbuyableReplacement, "Ornn Upgrade", inStore: false));
    }

    private static ItemVersion Item(int itemId, string name, bool inStore) => new()
    {
        ItemId = itemId,
        PatchVersion = Patch,
        Name = name,
        Description = name,
        Tags = ["Magic"],
        BuildsFrom = [],
        BuildsInto = [],
        InStore = inStore,
        PriceTotal = 3000
    };
}
