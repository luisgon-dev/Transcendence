using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Transcendence.Data;
using Transcendence.Data.Models.LoL.Account;
using Transcendence.Data.Models.LoL.Match;
using Transcendence.Data.Repositories.Implementations;

namespace Transcendence.IntegrationTests;

/// <summary>
/// Search autosuggest matches the normalized name with LIKE 'PREFIX%'. Under a non-C collation (prod and
/// this container use en_US.utf8) a default btree cannot serve LIKE, so every search read the whole
/// Summoners table. IX_Summoners_SearchNamePattern uses text_pattern_ops so the prefix becomes an index
/// range; these tests pin both the plan and the results.
/// </summary>
[Collection(PostgresIntegrationCollection.Name)]
public sealed class SummonerSearchIndexTests(PostgresIntegrationFixture fixture)
{
    [Fact]
    public async Task PrefixSearch_IsAnIndexRangeOnThePatternIndex()
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();

        await using (var collation = new NpgsqlCommand(
                         "SELECT datcollate FROM pg_database WHERE datname = current_database()", connection))
        {
            var collationName = (string)(await collation.ExecuteScalarAsync())!;
            new[] { "C", "POSIX" }.Should().NotContain(collationName,
                "the test only proves something when, as on prod, a default btree cannot serve LIKE");
        }

        // Realistic selectivity: a region of names the prefix mostly does not match. Rolled back, so the
        // shared fixture database is left as it was.
        await using var transaction = await connection.BeginTransactionAsync();
        await using (var seed = new NpgsqlCommand(
                         "INSERT INTO \"Summoners\" (\"Id\", \"Puuid\", \"PlatformRegion\", \"Region\", \"GameName\", " +
                         "\"TagLine\", \"GameNameNormalized\", \"TagLineNormalized\", \"SummonerLevel\", " +
                         "\"ProfileIconId\", \"RevisionDate\", \"UpdatedAt\") " +
                         "SELECT gen_random_uuid(), 'plan-' || g, 'NA1', 'AMERICAS', 'Name' || g, 'NA1', " +
                         "'NAME' || g, 'NA1', 1, 1, 0, now() FROM generate_series(1, 20000) g; " +
                         "ANALYZE \"Summoners\"; SET LOCAL enable_seqscan = off;",
                         connection, transaction))
            await seed.ExecuteNonQueryAsync();

        // The predicate shape SummonerRepository.SearchByPrefixAsync sends, with the pattern bound as a
        // parameter as EF does.
        await using var explain = new NpgsqlCommand(
            "EXPLAIN SELECT s.\"GameName\" FROM \"Summoners\" AS s " +
            "WHERE s.\"PlatformRegion\" = @region AND s.\"GameName\" IS NOT NULL AND s.\"TagLine\" IS NOT NULL " +
            "AND s.\"GameNameNormalized\" IS NOT NULL AND s.\"TagLineNormalized\" IS NOT NULL " +
            "AND s.\"GameNameNormalized\" LIKE @pattern ESCAPE ''",
            connection, transaction);
        explain.Parameters.AddWithValue("region", "NA1");
        explain.Parameters.AddWithValue("pattern", "KRO%");
        var plan = new List<string>();
        await using (var reader = await explain.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
                plan.Add(reader.GetString(0));
        }

        await transaction.RollbackAsync();

        var text = string.Join('\n', plan);
        text.Should().Contain("IX_Summoners_SearchNamePattern");
        text.Should().Contain("~>=~", "the LIKE prefix must become an index range, not a filter over the region");
    }

    [Fact]
    public async Task PrefixSearch_ReturnsOnlyStoredNamesWithThePrefix()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        await using (var db = CreateContext())
        {
            AddPlayedSummoner(db, $"Kro{suffix}", "NA1", "NA1");
            AddPlayedSummoner(db, $"Kro{suffix}x", "EUW", "NA1");
            AddPlayedSummoner(db, $"Kr{suffix}", "NA1", "NA1"); // shorter than the prefix
            AddPlayedSummoner(db, $"Kro{suffix}", "EUW", "EUW1"); // other region
            db.Summoners.Add(NewSummoner($"Kro{suffix}zz", "NA1", "NA1")); // never played: excluded
            await db.SaveChangesAsync();
        }

        await using var context = CreateContext();
        var repository = new SummonerRepository(context, new RankRepository(context));

        var results = await repository.SearchByPrefixAsync("NA1", $"kro{suffix}", null, 8);

        results.Select(x => x.GameName).Should().Equal($"Kro{suffix}", $"Kro{suffix}x");
        (await repository.SearchByPrefixAsync("NA1", $"kro{suffix}", "eu", 8))
            .Select(x => x.GameName).Should().Equal($"Kro{suffix}x");
    }

    private void AddPlayedSummoner(TranscendenceContext db, string gameName, string tagLine, string region)
    {
        var summoner = NewSummoner(gameName, tagLine, region);
        var match = new Match
        {
            Id = Guid.NewGuid(),
            MatchId = $"{region}_search_{Guid.NewGuid():N}",
            MatchDate = 1,
            Patch = "16.19",
            QueueId = 420,
            QueueFamily = "RANKED_SOLO_DUO",
            QueueType = "RANKED_SOLO_5x5",
            PlatformRegion = region,
            Status = FetchStatus.Success
        };
        db.Summoners.Add(summoner);
        db.MatchParticipants.Add(new MatchParticipant
        {
            Id = Guid.NewGuid(),
            Match = match,
            MatchId = match.Id,
            Summoner = summoner,
            SummonerId = summoner.Id,
            Puuid = summoner.Puuid,
            ParticipantId = 1,
            TeamId = 100
        });
    }

    private static Summoner NewSummoner(string gameName, string tagLine, string region) => new()
    {
        Id = Guid.NewGuid(),
        Puuid = $"search-{Guid.NewGuid():N}",
        PlatformRegion = region,
        Region = "AMERICAS",
        GameName = gameName,
        TagLine = tagLine,
        GameNameNormalized = gameName.ToUpperInvariant(),
        TagLineNormalized = tagLine.ToUpperInvariant(),
        UpdatedAt = DateTime.UtcNow
    };

    private TranscendenceContext CreateContext() =>
        new(new DbContextOptionsBuilder<TranscendenceContext>().UseNpgsql(fixture.ConnectionString).Options);
}
