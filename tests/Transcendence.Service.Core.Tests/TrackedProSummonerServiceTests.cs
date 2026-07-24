using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Transcendence.Data;
using Transcendence.Data.Models.LoL.Account;
using Transcendence.Service.Core.Services.ProSummoners.Implementations;
using Transcendence.Service.Core.Tests.Support;
using Transcendence.WebAPI.Controllers;

namespace Transcendence.Service.Core.Tests;

public class TrackedProSummonerServiceTests
{
    [Fact]
    public async Task CreateAsync_RejectsMissingRequiredFields()
    {
        await using var harness = await TrackedProHarness.CreateAsync();

        var result = await harness.Service.CreateAsync(new UpsertTrackedProSummonerRequest(" ", "Tag", "NA1"));

        result.IsSuccess.Should().BeFalse();
        result.ValidationError.Should().Be("gameName, tagLine, and platformRegion are required.");
    }

    [Fact]
    public async Task CreateAsync_RejectsUnsupportedPlatform()
    {
        await using var harness = await TrackedProHarness.CreateAsync();

        var result = await harness.Service.CreateAsync(new UpsertTrackedProSummonerRequest("Name", "Tag", "moon"));

        result.ValidationError.Should().Be("Unsupported platform region 'moon'.");
    }

    [Fact]
    public async Task CreateAsync_RejectsUnresolvableRiotIdWithoutPuuid()
    {
        await using var harness = await TrackedProHarness.CreateAsync();

        var result = await harness.Service.CreateAsync(new UpsertTrackedProSummonerRequest("Name", "Tag", "na1"));

        result.ValidationError.Should().Contain("Could not resolve Riot ID 'Name#Tag'");
    }

    [Fact]
    public async Task CreateAsync_RejectsDuplicatePuuidAndPlatform()
    {
        await using var harness = await TrackedProHarness.CreateAsync();
        harness.Db.TrackedProSummoners.Add(NewTracked("puuid-1"));
        await harness.Db.SaveChangesAsync();

        var result = await harness.Service.CreateAsync(
            new UpsertTrackedProSummonerRequest("Name", "Tag", "na1", " puuid-1 "));

        result.ValidationError.Should().Be("Tracked pro summoner already exists for this puuid/platform.");
    }

    [Fact]
    public async Task CreateAsync_ResolvesPuuidNormalizesFieldsAndPersists()
    {
        await using var harness = await TrackedProHarness.CreateAsync();
        harness.Db.Summoners.Add(new Summoner
        {
            Id = Guid.NewGuid(),
            Puuid = "resolved-puuid",
            GameName = "Name",
            TagLine = "Tag",
            GameNameNormalized = "NAME",
            TagLineNormalized = "TAG",
            PlatformRegion = "NA1",
            Region = "AMERICAS"
        });
        await harness.Db.SaveChangesAsync();

        var result = await harness.Service.CreateAsync(new UpsertTrackedProSummonerRequest(
            " Name ", " Tag ", " na1 ", ProName: " Pro ", TeamName: " "));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEquivalentTo(new
        {
            Puuid = "resolved-puuid",
            PlatformRegion = "NA1",
            GameName = "Name",
            TagLine = "Tag",
            ProName = "Pro",
            TeamName = (string?)null
        });
        (await harness.Db.TrackedProSummoners.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task UpdateAsync_WhenMissing_ReturnsNull()
    {
        await using var harness = await TrackedProHarness.CreateAsync();

        var result = await harness.Service.UpdateAsync(
            Guid.NewGuid(),
            new UpsertTrackedProSummonerRequest("Name", "Tag", "NA1"));

        result.Should().BeNull();
    }

    [Fact]
    public async Task UpdateAsync_OnlyOverwritesPuuidAndPlatformWhenProvided()
    {
        await using var harness = await TrackedProHarness.CreateAsync();
        var tracked = NewTracked("original-puuid");
        harness.Db.TrackedProSummoners.Add(tracked);
        await harness.Db.SaveChangesAsync();

        var result = await harness.Service.UpdateAsync(
            tracked.Id,
            new UpsertTrackedProSummonerRequest(" New Name ", " New Tag ", " ", " ", " Pro ", " Team "));

        result.Should().NotBeNull();
        result!.Puuid.Should().Be("original-puuid");
        result.PlatformRegion.Should().Be("NA1");
        result.GameName.Should().Be("New Name");
        result.ProName.Should().Be("Pro");
    }

    [Fact]
    public async Task DeleteAsync_ReturnsFalseForMissingAndTrueAfterDeletingExisting()
    {
        await using var harness = await TrackedProHarness.CreateAsync();
        var tracked = NewTracked("puuid-delete");
        harness.Db.TrackedProSummoners.Add(tracked);
        await harness.Db.SaveChangesAsync();

        (await harness.Service.DeleteAsync(Guid.NewGuid())).Should().BeFalse();
        (await harness.Service.DeleteAsync(tracked.Id)).Should().BeTrue();
        (await harness.Db.TrackedProSummoners.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task ApproveCandidateAsync_PreservesCompetitiveIdentityAndSource()
    {
        await using var harness = await TrackedProHarness.CreateAsync();
        var candidate = new ProPlayerDiscoveryCandidate
        {
            Id = Guid.NewGuid(),
            Source = "leaguepedia",
            ExternalId = "Faker",
            ProName = "Faker",
            TeamName = "T1",
            Role = "Mid",
            SoloQueueIds = "Hide on bush#KR1",
            Status = "pending"
        };
        harness.Db.ProPlayerDiscoveryCandidates.Add(candidate);
        await harness.Db.SaveChangesAsync();

        var result = await harness.Service.ApproveCandidateAsync(
            candidate.Id,
            new ApproveProPlayerCandidateRequest("Hide on bush", "KR1", "KR", "faker-puuid"));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEquivalentTo(new
        {
            Puuid = "faker-puuid",
            PlatformRegion = "KR",
            ProName = "Faker",
            TeamName = "T1",
            IsPro = true,
            Source = "leaguepedia",
            SourceExternalId = "Faker"
        });
        candidate.Status.Should().Be("approved");
        candidate.ApprovedTrackedProSummonerId.Should().Be(result.Value!.Id);
    }

    private static TrackedProSummoner NewTracked(string puuid) => new()
    {
        Id = Guid.NewGuid(),
        Puuid = puuid,
        PlatformRegion = "NA1",
        GameName = "Name",
        TagLine = "Tag",
        CreatedAtUtc = DateTime.UtcNow,
        UpdatedAtUtc = DateTime.UtcNow
    };

    private sealed class TrackedProHarness : IAsyncDisposable
    {
        private TrackedProHarness(SqliteConnection connection, SqliteCompatibleTranscendenceContext db)
        {
            Connection = connection;
            Db = db;
            Service = new TrackedProSummonerService(db);
        }

        private SqliteConnection Connection { get; }
        public SqliteCompatibleTranscendenceContext Db { get; }
        public TrackedProSummonerService Service { get; }

        public static async Task<TrackedProHarness> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<TranscendenceContext>()
                .UseSqlite(connection)
                .Options;
            var db = new SqliteCompatibleTranscendenceContext(options);
            await db.Database.EnsureCreatedAsync();
            return new TrackedProHarness(connection, db);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await Connection.DisposeAsync();
        }
    }
}
