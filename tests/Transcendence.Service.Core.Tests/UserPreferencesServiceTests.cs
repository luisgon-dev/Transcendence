using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using Transcendence.Data;
using Transcendence.Data.Models.Auth;
using Transcendence.Data.Models.LiveGame;
using Transcendence.Data.Repositories.Implementations;
using Transcendence.Data.Repositories.Interfaces;
using Transcendence.Service.Core.Services.Auth.Implementations;
using Transcendence.Service.Core.Services.RiotApi;
using Transcendence.Service.Core.Tests.Support;

namespace Transcendence.Service.Core.Tests;

public sealed class UserPreferencesServiceTests
{
    [Fact]
    public async Task GetFavorites_marks_only_fresh_in_game_snapshots_live()
    {
        var now = DateTime.UtcNow;
        var repository = new Mock<IUserPreferencesRepository>();
        repository.Setup(x => x.GetFavoritesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                Favorite("fresh", "in_game", now.AddMinutes(-2)),
                Favorite("stale", "in_game", now.AddMinutes(-12)),
                Favorite("offline", "offline", now.AddMinutes(-1))
            ]);
        var service = new UserPreferencesService(repository.Object, Mock.Of<ISummonerRepository>());

        var result = await service.GetFavoritesAsync(Guid.NewGuid());

        result.Single(x => x.DisplayName == "fresh#NA1").IsLive.Should().BeTrue();
        result.Single(x => x.DisplayName == "stale#NA1").IsLive.Should().BeFalse();
        result.Single(x => x.DisplayName == "offline#NA1").IsLive.Should().BeFalse();
    }

    [Fact]
    public async Task Repository_returns_only_the_latest_snapshot_for_each_favorite()
    {
        await using var harness = await FavoritesHarness.CreateAsync();
        var accountId = Guid.NewGuid();
        var summonerId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        harness.Db.UserAccounts.Add(new UserAccount
        {
            Id = accountId,
            Email = "player@example.com",
            EmailNormalized = "PLAYER@EXAMPLE.COM",
            PasswordHash = "hash"
        });
        harness.Db.UserFavoriteSummoners.Add(new UserFavoriteSummoner
        {
            Id = Guid.NewGuid(),
            UserAccountId = accountId,
            SummonerPuuid = "favorite-puuid",
            PlatformRegion = "NA1",
            DisplayName = "Favorite#NA1",
            CreatedAtUtc = now
        });
        harness.Db.LiveGameSnapshots.AddRange(
            Snapshot(summonerId, "offline", null, now.AddMinutes(-3)),
            Snapshot(summonerId, "in_game", "game-123", now.AddMinutes(-1)));
        await harness.Db.SaveChangesAsync();

        var result = await harness.Repository.GetFavoritesAsync(accountId);

        result.Should().ContainSingle();
        result[0].LiveState.Should().Be("in_game");
        result[0].LiveGameId.Should().Be("game-123");
        result[0].LiveObservedAtUtc.Should().BeCloseTo(now.AddMinutes(-1), TimeSpan.FromSeconds(1));
    }

    private static FavoriteSummonerReadModel Favorite(string name, string state, DateTime observedAtUtc) =>
        new(
            Guid.NewGuid(),
            $"{name}-puuid",
            "NA1",
            $"{name}#NA1",
            DateTime.UtcNow,
            state,
            state == "in_game" ? $"{name}-game" : null,
            observedAtUtc);

    private static LiveGameSnapshot Snapshot(
        Guid summonerId,
        string state,
        string? gameId,
        DateTime observedAtUtc) => new()
        {
            Id = Guid.NewGuid(),
            SummonerId = summonerId,
            Puuid = "favorite-puuid",
            PlatformRegion = "NA1",
            State = state,
            GameId = gameId,
            ObservedAtUtc = observedAtUtc,
            NextPollAtUtc = observedAtUtc.AddMinutes(1)
        };

    private sealed class FavoritesHarness : IAsyncDisposable
    {
        private FavoritesHarness(
            SqliteConnection connection,
            SqliteCompatibleTranscendenceContext db)
        {
            Connection = connection;
            Db = db;
            Repository = new UserPreferencesRepository(db);
        }

        private SqliteConnection Connection { get; }
        public SqliteCompatibleTranscendenceContext Db { get; }
        public UserPreferencesRepository Repository { get; }

        public static async Task<FavoritesHarness> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<TranscendenceContext>()
                .UseSqlite(connection)
                .Options;
            var db = new SqliteCompatibleTranscendenceContext(options);
            await db.Database.EnsureCreatedAsync();
            return new FavoritesHarness(connection, db);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await Connection.DisposeAsync();
        }
    }
}
