using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Transcendence.Data;
using Transcendence.Data.Models.LoL.Account;
using Transcendence.Service.Core.Services.Jobs;
using Transcendence.Service.Core.Tests.Support;
using State = Transcendence.Service.Core.Services.Jobs.AddOrUpdateHighEloProfiles.ApexLeagueState;

namespace Transcendence.Service.Core.Tests;

/// <summary>
/// The two-hourly apex refresh skips players whose ladder entry has not moved since their last
/// refresh -- three Riot calls each, spent from the budget match ingestion needs -- but must still
/// re-fetch anyone who played, anyone not stored, and everyone at least once a day.
/// </summary>
public class HighEloRefreshSkipTests
{
    private static readonly DateTime Now = new(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task OnlyAFreshProfileWithAnIdenticalSoloEntry_IsSkipped()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<TranscendenceContext>().UseSqlite(connection).Options;
        await using var db = new SqliteCompatibleTranscendenceContext(options);
        await db.Database.EnsureCreatedAsync();

        Add(db, "unchanged", Now.AddHours(-2), solo: ("CHALLENGER", "I", 1200, 300, 250));
        Add(db, "played", Now.AddHours(-2), solo: ("CHALLENGER", "I", 1180, 300, 249));
        Add(db, "stale", Now.AddDays(-2), solo: ("GRANDMASTER", "I", 700, 200, 190));
        Add(db, "flex-only", Now.AddHours(-2), solo: null);
        await db.SaveChangesAsync();

        var ladder = new Dictionary<string, State>
        {
            ["unchanged"] = new("unchanged", "CHALLENGER", "I", 1200, 300, 250),
            ["played"] = new("played", "CHALLENGER", "I", 1200, 300, 250),
            ["stale"] = new("stale", "GRANDMASTER", "I", 700, 200, 190),
            ["flex-only"] = new("flex-only", "MASTER", "I", 100, 50, 50),
            ["new"] = new("new", "MASTER", "I", 90, 40, 38)
        };

        var skipped = await AddOrUpdateHighEloProfiles.FindUnchangedSinceLastRefreshAsync(
            db, ladder, Now, CancellationToken.None);

        skipped.Should().BeEquivalentTo(["unchanged"],
            "a game changes the entry, an unknown or day-old profile is always re-fetched");
    }

    private static void Add(
        TranscendenceContext db, string puuid, DateTime updatedAt,
        (string Tier, string Division, int Lp, int Wins, int Losses)? solo)
    {
        var summoner = new Summoner
        {
            Id = Guid.NewGuid(),
            Puuid = puuid,
            PlatformRegion = "EUW1",
            Region = "EUROPE",
            GameName = puuid,
            TagLine = "EUW",
            UpdatedAt = updatedAt
        };
        db.Summoners.Add(summoner);
        db.Ranks.Add(new Rank
        {
            Id = Guid.NewGuid(),
            SummonerId = summoner.Id,
            Summoner = summoner,
            QueueType = solo is null ? "RANKED_FLEX_SR" : "RANKED_SOLO_5x5",
            Tier = solo?.Tier ?? "DIAMOND",
            RankNumber = solo?.Division ?? "II",
            LeaguePoints = solo?.Lp ?? 10,
            Wins = solo?.Wins ?? 5,
            Losses = solo?.Losses ?? 5
        });
    }
}
