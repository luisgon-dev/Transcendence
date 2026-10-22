using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Transcendence.Data;
using Transcendence.Data.Models.LoL.Account;
using Transcendence.Data.Models.LoL.Match;
using Transcendence.Data.Repositories.Implementations;
using Transcendence.Service.Core.Tests.Support;

namespace Transcendence.Service.Core.Tests;

// Production: a failed batch insert falls back to ChangeTracker.Clear() and one match per save. Re-adding
// a detached match used to insert its participants' existing summoners (PK_Summoners), and through
// Summoner.MatchParticipants every other match of the batch, so the whole batch was lost.
public sealed class MatchRepositoryFallbackTests
{
    [Fact]
    public async Task AddMatchAsync_AfterClear_InsertsOnlyThatMatchAndReusesExistingSummoners()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<TranscendenceContext>().UseSqlite(connection).Options;
        await using var db = new SqliteCompatibleTranscendenceContext(options);
        await db.Database.EnsureCreatedAsync();

        var summoner = new Summoner { Id = Guid.NewGuid(), Puuid = "shared", PlatformRegion = "NA1", Region = "AMERICAS" };
        db.Summoners.Add(summoner);
        await db.SaveChangesAsync();

        var repository = new MatchRepository(db);
        var first = NewMatch("NA1_1", summoner);
        var second = NewMatch("NA1_2", summoner);
        await repository.AddMatchAsync(first, CancellationToken.None);
        await repository.AddMatchAsync(second, CancellationToken.None);
        summoner.MatchParticipants.Should().HaveCount(2, "fixup cross-links the batch through the shared summoner");

        // The batch save failed; fall back to one match per save.
        db.ChangeTracker.Clear();
        await repository.AddMatchAsync(first, CancellationToken.None);
        await db.SaveChangesAsync();
        (await db.Matches.Select(m => m.MatchId).ToListAsync()).Should().Equal("NA1_1");

        await repository.AddMatchAsync(second, CancellationToken.None);
        await db.SaveChangesAsync();
        (await db.Matches.CountAsync()).Should().Be(2);
        (await db.Summoners.CountAsync()).Should().Be(1);
        (await db.MatchParticipants.CountAsync(p => p.SummonerId == summoner.Id)).Should().Be(2);
    }

    private static Match NewMatch(string matchId, Summoner summoner)
    {
        var match = new Match { Id = Guid.NewGuid(), MatchId = matchId, Patch = "16.20", QueueId = 420, Status = FetchStatus.Success };
        match.Participants.Add(new MatchParticipant
        {
            Id = Guid.NewGuid(), Match = match, Summoner = summoner, SummonerId = summoner.Id,
            ParticipantId = 1, ChampionId = 1, TeamId = 100
        });
        return match;
    }
}
