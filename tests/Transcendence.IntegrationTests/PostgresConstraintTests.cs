using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Transcendence.Data;
using Transcendence.Data.Models.LoL.Account;
using Transcendence.Data.Models.LoL.Match;

namespace Transcendence.IntegrationTests;

/// <summary>
/// Exercises the constraint- and query-filter-sensitive behaviors the unit suite cannot enforce: the
/// EF InMemory provider ignores unique indexes and FK cascades entirely, and SQLite enforces them only
/// partially. These prove the real Postgres schema behaves as the model intends (finding: "test harness
/// never exercises the real Postgres DDL and only partially enforces relational constraints").
/// Each test scopes to unique identifiers so it is isolated on the shared container.
/// </summary>
[Collection(PostgresIntegrationCollection.Name)]
public sealed class PostgresConstraintTests(PostgresIntegrationFixture fixture)
{
    private TranscendenceContext NewDb() =>
        new(new DbContextOptionsBuilder<TranscendenceContext>()
            .UseNpgsql(fixture.ConnectionString)
            .Options);

    [Fact]
    public async Task DuplicatePuuid_ViolatesUniqueIndex_OnPostgres()
    {
        var puuid = Guid.NewGuid().ToString("N");
        await using var db = NewDb();

        db.Summoners.Add(MakeSummoner(puuid));
        await db.SaveChangesAsync();

        db.Summoners.Add(MakeSummoner(puuid));   // same non-null Puuid, different Id
        var act = () => db.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>(
            "the unique index on Summoner.Puuid must be enforced by Postgres — the InMemory provider would silently accept the duplicate");
    }

    [Fact]
    public async Task StableRiotIdentifiers_CannotBeNull_OnPostgres()
    {
        await using var db = NewDb();

        var nullSummonerPuuid = async () => await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "Summoners" ("Id", "Puuid", "PlatformRegion", "Region")
            VALUES ({Guid.NewGuid()}, NULL, 'NA1', 'americas')
            """);
        var nullMatchId = async () => await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "Matches" ("Id", "MatchId", "MatchDate", "Duration", "QueueId", "Status", "RetryCount")
            VALUES ({Guid.NewGuid()}, NULL, 0, 0, 0, 0, 0)
            """);

        (await nullSummonerPuuid.Should().ThrowAsync<PostgresException>())
            .Which.SqlState.Should().Be(PostgresErrorCodes.NotNullViolation);
        (await nullMatchId.Should().ThrowAsync<PostgresException>())
            .Which.SqlState.Should().Be(PostgresErrorCodes.NotNullViolation);
    }

    [Fact]
    public async Task MatchFacts_DoNotRequireStaticDataRows_OnPostgres()
    {
        var patch = $"missing-{Guid.NewGuid():N}";
        await using var db = NewDb();
        var summoner = MakeSummoner(Guid.NewGuid().ToString("N"));
        var match = MakeMatch(FetchStatus.Success, patch);
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
            ChampionId = 100,
            Win = true
        };
        participant.Items.Add(new MatchParticipantItem
        {
            MatchParticipantId = participant.Id,
            MatchParticipant = participant,
            SlotIndex = 0,
            ItemId = 999_001,
            PatchVersion = patch
        });
        participant.Runes.Add(new MatchParticipantRune
        {
            MatchParticipantId = participant.Id,
            MatchParticipant = participant,
            RuneId = 999_002,
            PatchVersion = patch,
            SelectionTree = RuneSelectionTree.Primary,
            SelectionIndex = 0,
            StyleId = 999_003
        });

        db.MatchParticipants.Add(participant);
        await db.SaveChangesAsync();

        (await db.MatchParticipantItems.CountAsync(x => x.MatchParticipantId == participant.Id))
            .Should().Be(1, "partial item metadata must not reject immutable match facts");
        (await db.MatchParticipantRunes.CountAsync(x => x.MatchParticipantId == participant.Id))
            .Should().Be(1, "partial rune metadata must not reject immutable match facts");
    }

    [Fact]
    public async Task DeletingMatch_CascadeDeletesParticipants_ViaPostgresForeignKey()
    {
        Guid matchId;
        Guid participantId;
        await using (var db = NewDb())
        {
            var summoner = MakeSummoner(Guid.NewGuid().ToString("N"));
            var match = MakeMatch(FetchStatus.Success, patch: Guid.NewGuid().ToString("N"));
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
                ChampionId = 100,
                TeamPosition = "TOP",
                Win = true
            };
            db.Summoners.Add(summoner);
            db.Matches.Add(match);
            db.MatchParticipants.Add(participant);
            await db.SaveChangesAsync();
            matchId = match.Id;
            participantId = participant.Id;
        }

        await using (var db = NewDb())
        {
            // Remove the Match without loading its participants: the deletion of the participant must be
            // performed by the database's ON DELETE CASCADE, not by EF change-tracking.
            var match = await db.Matches.SingleAsync(m => m.Id == matchId);
            db.Matches.Remove(match);
            await db.SaveChangesAsync();
        }

        await using (var readDb = NewDb())
        {
            (await readDb.MatchParticipants.IgnoreQueryFilters().CountAsync(p => p.Id == participantId))
                .Should().Be(0, "deleting a Match must cascade-delete its participants via the Postgres FK");
        }
    }

    [Fact]
    public async Task PermanentlyUnfetchableMatches_AreHiddenByGlobalQueryFilter_OnPostgres()
    {
        var patch = Guid.NewGuid().ToString("N");
        await using var db = NewDb();

        db.Matches.Add(MakeMatch(FetchStatus.Success, patch));
        db.Matches.Add(MakeMatch(FetchStatus.PermanentlyUnfetchable, patch));
        await db.SaveChangesAsync();

        (await db.Matches.CountAsync(m => m.Patch == patch))
            .Should().Be(1, "the global query filter must hide PermanentlyUnfetchable matches from default reads");
        (await db.Matches.IgnoreQueryFilters().CountAsync(m => m.Patch == patch))
            .Should().Be(2, "IgnoreQueryFilters must surface the unfetchable row, proving it was persisted, not rejected");
    }

    private static Summoner MakeSummoner(string puuid) => new()
    {
        Id = Guid.NewGuid(),
        PlatformRegion = "NA1",
        Region = "americas",
        GameName = Guid.NewGuid().ToString("N")[..8],
        TagLine = "NA1",
        Puuid = puuid,
        SummonerName = "s",
        RiotSummonerId = Guid.NewGuid().ToString("N")
    };

    private static Match MakeMatch(FetchStatus status, string patch) => new()
    {
        Id = Guid.NewGuid(),
        MatchId = Guid.NewGuid().ToString("N"),
        MatchDate = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        Duration = 1800,
        Patch = patch,
        QueueId = 420,
        QueueFamily = "RANKED_SOLO_DUO",
        QueueType = "420",
        Status = status,
        PlatformRegion = "NA1",
        FetchedAt = DateTime.UtcNow
    };
}
