using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Transcendence.Data;
using Transcendence.Data.Models.LoL.Match;
using Transcendence.Data.Models.LoL.Static;
using Transcendence.Service.Core.Services.Analytics.Implementations;
using Transcendence.Service.Core.Tests.Support;
using MatchEntity = Transcendence.Data.Models.LoL.Match.Match;

namespace Transcendence.Service.Core.Tests;

public class AnalyticsPatchQueryServiceTests
{
    [Fact]
    public async Task GetPatchOptionsAsync_MergesMetadataAndRankedMatches_DeduplicatesAndOrders()
    {
        await using var harness = await AnalyticsPatchHarness.CreateAsync();
        var now = DateTime.UtcNow;
        harness.Db.Patches.AddRange(
            new Patch { Version = "15.2", ReleaseDate = now.AddHours(-2), DetectedAt = now.AddHours(-1), IsActive = true },
            new Patch { Version = "15.1", ReleaseDate = now.AddDays(-14), DetectedAt = now.AddDays(-14), IsActive = false },
            new Patch { Version = "14.24", ReleaseDate = now.AddDays(-28), DetectedAt = now.AddDays(-28), IsActive = false });
        harness.Db.Matches.AddRange(
            NewMatch("15.1", 420, "RANKED_SOLO_5x5", FetchStatus.Success),
            NewMatch("15.1", 0, "420", FetchStatus.Success),
            NewMatch("14.24", 420, "RANKED_SOLO_5x5", FetchStatus.TemporaryFailure),
            NewMatch("13.20", 420, "RANKED_SOLO_5x5", FetchStatus.Success),
            NewMatch("12.1", 440, "RANKED_FLEX_SR", FetchStatus.Success));
        await harness.Db.SaveChangesAsync();

        var result = await harness.Service.GetPatchOptionsAsync();

        result.Select(x => x.Patch).Should().Equal("15.2", "15.1", "13.20");
        result.Should().ContainSingle(x => x.Patch == "15.1")
            .Which.RankedSoloDuoMatchCount.Should().Be(2);
        result.Single(x => x.Patch == "13.20").ReleasedAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task GetActivePatchStatusAsync_WhenNoActivePatch_ReturnsEmptyContract()
    {
        await using var harness = await AnalyticsPatchHarness.CreateAsync();

        var result = await harness.Service.GetActivePatchStatusAsync();

        result.Patch.Should().BeNull();
        result.ActivePatchReleasedAtUtc.Should().BeNull();
        result.ActivePatchDetectedAtUtc.Should().BeNull();
    }

    private static MatchEntity NewMatch(string patch, int queueId, string queueType, FetchStatus status) => new()
    {
        Id = Guid.NewGuid(),
        MatchId = $"NA1_{Guid.NewGuid():N}",
        Patch = patch,
        QueueId = queueId,
        QueueType = queueType,
        Status = status
    };

    private sealed class AnalyticsPatchHarness : IAsyncDisposable
    {
        private AnalyticsPatchHarness(
            SqliteConnection connection,
            SqliteCompatibleTranscendenceContext db)
        {
            Connection = connection;
            Db = db;
            Service = new AnalyticsPatchQueryService(db);
        }

        private SqliteConnection Connection { get; }
        public SqliteCompatibleTranscendenceContext Db { get; }
        public AnalyticsPatchQueryService Service { get; }

        public static async Task<AnalyticsPatchHarness> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<TranscendenceContext>()
                .UseSqlite(connection)
                .Options;
            var db = new SqliteCompatibleTranscendenceContext(options);
            await db.Database.EnsureCreatedAsync();
            return new AnalyticsPatchHarness(connection, db);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await Connection.DisposeAsync();
        }
    }
}
