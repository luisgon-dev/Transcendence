using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Transcendence.Data;
using Transcendence.Data.Models.LoL.Account;
using Transcendence.Data.Models.LoL.Match;
using Transcendence.Data.Models.LoL.Static;
using Transcendence.Service.Core.Services.Analytics.Implementations;
using Transcendence.Service.Core.Services.Analytics.Models;
using Transcendence.Service.Core.Tests.Support;

namespace Transcendence.Service.Core.Tests;

public class ChampionAnalyticsComputeServiceTests
{
    [Fact]
    public async Task ComputeTierListAsync_DoesNotPopulatePatchMovementFromPreviousPatchHotPath()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<TranscendenceContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new SqliteCompatibleTranscendenceContext(options);
        await db.Database.EnsureCreatedAsync();

        db.Patches.AddRange(
            new Patch
            {
                Version = "15.1",
                ReleaseDate = DateTime.UtcNow.AddDays(-21),
                DetectedAt = DateTime.UtcNow.AddDays(-21),
                IsActive = false
            },
            new Patch
            {
                Version = "15.2",
                ReleaseDate = DateTime.UtcNow.AddDays(-2),
                DetectedAt = DateTime.UtcNow.AddDays(-2),
                IsActive = true
            });

        SeedMatch(db, "15.1", "NA1_1", summonerName: "PreviousPatch", championId: 266, role: "TOP", win: true);
        SeedMatch(db, "15.2", "NA1_2", summonerName: "CurrentPatch", championId: 266, role: "TOP", win: true);
        await db.SaveChangesAsync();

        var service = new ChampionAnalyticsComputeService(
            db,
            Options.Create(new ChampionAnalyticsComputeOptions
            {
                MinimumGamesRequired = 1,
                EarlyPatchMinimumGamesRequired = 1,
                BootstrapPatchMinimumGamesRequired = 1,
                BootstrapWindowHours = 24,
                ProvisionalWindowHours = 96,
                MaturingWindowHours = 240
            }),
            NullLogger<ChampionAnalyticsComputeService>.Instance);

        var result = await service.ComputeTierListAsync("TOP", null, null, "15.2", CancellationToken.None);

        result.Should().ContainSingle();
        result[0].Movement.Should().BeNull();
        result[0].PreviousTier.Should().BeNull();
    }

    [Fact]
    public async Task ComputeProChampionPlayrateAsync_RanksChampionsAndHonorsScope()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<TranscendenceContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new SqliteCompatibleTranscendenceContext(options);
        await db.Database.EnsureCreatedAsync();

        db.Patches.Add(new Patch
        {
            Version = "15.2",
            ReleaseDate = DateTime.UtcNow.AddDays(-2),
            DetectedAt = DateTime.UtcNow.AddDays(-2),
            IsActive = true
        });

        // One official pro and one auto-discovered high-elo one-trick.
        db.TrackedProSummoners.Add(new TrackedProSummoner
        {
            Id = Guid.NewGuid(),
            Puuid = "pro-puuid",
            PlatformRegion = "NA1",
            ProName = "Faker",
            TeamName = "T1",
            IsPro = true,
            IsHighEloOtp = false,
            IsActive = true
        });
        db.TrackedProSummoners.Add(new TrackedProSummoner
        {
            Id = Guid.NewGuid(),
            Puuid = "otp-puuid",
            PlatformRegion = "NA1",
            IsPro = false,
            IsHighEloOtp = true,
            IsActive = true
        });

        // Pro: champion 266 twice (1W/1L). OTP: champion 64 once (W).
        var pro = SeedProSummoner(db, "pro-puuid", "Faker");
        var otp = SeedProSummoner(db, "otp-puuid", "Otp");
        SeedParticipantMatch(db, "15.2", "NA1_1", pro, championId: 266, role: "TOP", win: true);
        SeedParticipantMatch(db, "15.2", "NA1_2", pro, championId: 266, role: "TOP", win: false);
        SeedParticipantMatch(db, "15.2", "NA1_3", otp, championId: 64, role: "JUNGLE", win: true);
        await db.SaveChangesAsync();

        var service = CreateComputeService(db);

        var proOnly = await service.ComputeProChampionPlayrateAsync(null, "pro", "15.2", CancellationToken.None);
        proOnly.Champions.Should().ContainSingle();
        proOnly.Champions[0].ChampionId.Should().Be(266);
        proOnly.Champions[0].Games.Should().Be(2);
        proOnly.Champions[0].Wins.Should().Be(1);
        proOnly.Champions[0].WinRate.Should().BeApproximately(0.5, 0.0001);
        proOnly.Champions[0].UniquePlayers.Should().Be(1);

        var highEloOnly = await service.ComputeProChampionPlayrateAsync(null, "highelo", "15.2", CancellationToken.None);
        highEloOnly.Champions.Should().ContainSingle();
        highEloOnly.Champions[0].ChampionId.Should().Be(64);

        var all = await service.ComputeProChampionPlayrateAsync(null, "all", "15.2", CancellationToken.None);
        all.Champions.Select(c => c.ChampionId).Should().BeEquivalentTo(new[] { 266, 64 });
        all.Champions[0].ChampionId.Should().Be(266); // most games first
    }

    [Fact]
    public async Task ComputeProBuildsAsync_HonorsRosterScope()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<TranscendenceContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new SqliteCompatibleTranscendenceContext(options);
        await db.Database.EnsureCreatedAsync();

        db.Patches.Add(new Patch
        {
            Version = "15.2",
            ReleaseDate = DateTime.UtcNow.AddDays(-2),
            DetectedAt = DateTime.UtcNow.AddDays(-2),
            IsActive = true
        });

        db.TrackedProSummoners.Add(new TrackedProSummoner
        {
            Id = Guid.NewGuid(),
            Puuid = "pro-puuid",
            PlatformRegion = "NA1",
            ProName = "Pro",
            TeamName = "Team",
            IsPro = true,
            IsHighEloOtp = false,
            IsActive = true
        });
        db.TrackedProSummoners.Add(new TrackedProSummoner
        {
            Id = Guid.NewGuid(),
            Puuid = "otp-puuid",
            PlatformRegion = "NA1",
            IsPro = false,
            IsHighEloOtp = true,
            IsActive = true
        });

        var pro = SeedProSummoner(db, "pro-puuid", "Pro");
        var otp = SeedProSummoner(db, "otp-puuid", "Otp");
        SeedParticipantMatch(db, "15.2", "NA1_1", pro, championId: 266, role: "TOP", win: true);
        SeedParticipantMatch(db, "15.2", "NA1_2", otp, championId: 266, role: "TOP", win: true);
        await db.SaveChangesAsync();

        var service = CreateComputeService(db);

        var proOnly = await service.ComputeProBuildsAsync(266, null, "TOP", "pro", "15.2", CancellationToken.None);
        proOnly.Scope.Should().Be("pro");
        proOnly.RecentProMatches.Should().ContainSingle();
        proOnly.TopPlayers.Should().ContainSingle(p => p.PlayerName == "Pro");

        var highEloOnly = await service.ComputeProBuildsAsync(266, null, "TOP", "highelo", "15.2", CancellationToken.None);
        highEloOnly.Scope.Should().Be("highelo");
        highEloOnly.RecentProMatches.Should().ContainSingle();
        highEloOnly.TopPlayers.Should().ContainSingle(p => p.PlayerName == "Otp#NA1");

        var all = await service.ComputeProBuildsAsync(266, null, "TOP", "all", "15.2", CancellationToken.None);
        all.Scope.Should().Be("all");
        all.RecentProMatches.Should().HaveCount(2);
        all.TopPlayers.Select(p => p.PlayerName).Should().BeEquivalentTo(new[] { "Pro", "Otp#NA1" });
    }

    private static ChampionAnalyticsComputeService CreateComputeService(TranscendenceContext db) =>
        new(
            db,
            Options.Create(new ChampionAnalyticsComputeOptions
            {
                MinimumGamesRequired = 1,
                EarlyPatchMinimumGamesRequired = 1,
                BootstrapPatchMinimumGamesRequired = 1,
                BootstrapWindowHours = 24,
                ProvisionalWindowHours = 96,
                MaturingWindowHours = 240
            }),
            NullLogger<ChampionAnalyticsComputeService>.Instance);

    private static Summoner SeedProSummoner(TranscendenceContext db, string puuid, string name, string platform = "NA1")
    {
        var summoner = new Summoner
        {
            Id = Guid.NewGuid(),
            PlatformRegion = platform,
            Region = "americas",
            GameName = name,
            TagLine = platform,
            Puuid = puuid,
            SummonerName = name,
            RiotSummonerId = Guid.NewGuid().ToString("N")
        };
        db.Summoners.Add(summoner);
        return summoner;
    }

    private static void SeedParticipantMatch(
        TranscendenceContext db,
        string patch,
        string matchId,
        Summoner summoner,
        int championId,
        string role,
        bool win)
    {
        var match = new Transcendence.Data.Models.LoL.Match.Match
        {
            Id = Guid.NewGuid(),
            MatchId = matchId,
            MatchDate = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Duration = 1800,
            Patch = patch,
            QueueId = 420,
            QueueFamily = "RANKED_SOLO_DUO",
            QueueType = "420",
            Status = FetchStatus.Success,
            PlatformRegion = summoner.PlatformRegion,
            FetchedAt = DateTime.UtcNow
        };

        var participant = new MatchParticipant
        {
            Id = Guid.NewGuid(),
            MatchId = match.Id,
            Match = match,
            SummonerId = summoner.Id,
            Summoner = summoner,
            Puuid = summoner.Puuid,
            ParticipantId = 1,
            TeamId = 100,
            ChampionId = championId,
            TeamPosition = role,
            Win = win
        };

        db.Matches.Add(match);
        db.MatchParticipants.Add(participant);
    }

    private static void SeedMatch(
        TranscendenceContext db,
        string patch,
        string matchId,
        string summonerName,
        int championId,
        string role,
        bool win)
    {
        var summoner = new Summoner
        {
            Id = Guid.NewGuid(),
            PlatformRegion = "NA1",
            Region = "americas",
            GameName = summonerName,
            TagLine = "NA1",
            Puuid = Guid.NewGuid().ToString("N"),
            SummonerName = summonerName,
            RiotSummonerId = Guid.NewGuid().ToString("N")
        };

        var match = new Transcendence.Data.Models.LoL.Match.Match
        {
            Id = Guid.NewGuid(),
            MatchId = matchId,
            MatchDate = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Duration = 1800,
            Patch = patch,
            QueueId = 420,
            QueueFamily = "RANKED_SOLO_DUO",
            QueueType = "420",
            Status = FetchStatus.Success,
            PlatformRegion = "NA1",
            FetchedAt = DateTime.UtcNow
        };

        var participant = new MatchParticipant
        {
            Id = Guid.NewGuid(),
            MatchId = match.Id,
            Match = match,
            SummonerId = summoner.Id,
            Summoner = summoner,
            Puuid = summoner.Puuid,
            ParticipantId = 1,
            TeamId = 100,
            ChampionId = championId,
            TeamPosition = role,
            Win = win
        };

        db.Summoners.Add(summoner);
        db.Matches.Add(match);
        db.MatchParticipants.Add(participant);
    }
}
