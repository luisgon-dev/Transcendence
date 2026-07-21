using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Transcendence.Data;
using Transcendence.Data.Models.LoL.Account;
using Transcendence.Data.Models.LoL.Match;
using Transcendence.Data.Models.LoL.Static;
using Transcendence.Service.Core.Services.Analytics.Implementations;
using Transcendence.Service.Core.Tests.Support;
using MatchEntity = Transcendence.Data.Models.LoL.Match.Match;

namespace Transcendence.Service.Core.Tests;

public sealed class ChampionSynergyServiceTests
{
    [Fact]
    public async Task TopSynergies_OnlyCountSameTeamJunglePartners()
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
            Version = "16.14",
            ReleaseDate = DateTime.UtcNow.AddDays(-4),
            DetectedAt = DateTime.UtcNow.AddDays(-4),
            IsActive = true
        });

        for (var index = 0; index < 4; index++)
        {
            var match = new MatchEntity
            {
                Id = Guid.NewGuid(),
                MatchId = $"NA1_SYNERGY_{index}",
                Patch = "16.14",
                QueueId = 420,
                QueueFamily = "RANKED_SOLO_DUO",
                Status = FetchStatus.Success,
                PlatformRegion = "NA1"
            };
            db.Matches.Add(match);
            var focalWin = index < 3;
            AddParticipant(db, match, index * 10 + 1, 266, "TOP", 100, focalWin);
            AddParticipant(db, match, index * 10 + 2, 64, "JUNGLE", 100, focalWin);
            AddParticipant(db, match, index * 10 + 3, 40, "UTILITY", 100, focalWin);
            AddParticipant(db, match, index * 10 + 4, 120, "JUNGLE", 200, !focalWin);
        }
        await db.SaveChangesAsync();

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging();
        serviceCollection.AddHybridCache();
        var services = serviceCollection.BuildServiceProvider();
        var service = new ChampionSynergyService(
            db,
            services.GetRequiredService<HybridCache>(),
            new AnalyticsPatchQueryService(db));

        var result = await service.GetSynergiesAsync(266, "TOP", null, "NA1", "solo", null);

        result.TotalGames.Should().Be(4);
        result.BaselineWinRate.Should().BeApproximately(0.75, 0.0001);
        var partner = result.BestPartners.Should().ContainSingle().Subject;
        partner.PartnerChampionId.Should().Be(64);
        partner.PartnerRole.Should().Be("JUNGLE");
        partner.Games.Should().Be(4);
        partner.PickRate.Should().BeApproximately(1, 0.0001);
        partner.WinRateDelta.Should().BeApproximately(0, 0.0001);
    }

    private static void AddParticipant(
        TranscendenceContext db,
        MatchEntity match,
        int participantId,
        int championId,
        string role,
        int teamId,
        bool win)
    {
        var summoner = new Summoner
        {
            Id = Guid.NewGuid(),
            Puuid = $"synergy-{match.MatchId}-{participantId}",
            PlatformRegion = "NA1",
            Region = "AMERICAS"
        };
        db.Summoners.Add(summoner);
        db.MatchParticipants.Add(new MatchParticipant
        {
            Id = Guid.NewGuid(),
            MatchId = match.Id,
            Match = match,
            SummonerId = summoner.Id,
            Summoner = summoner,
            ParticipantId = participantId,
            ChampionId = championId,
            TeamPosition = role,
            TeamId = teamId,
            Win = win
        });
    }
}
