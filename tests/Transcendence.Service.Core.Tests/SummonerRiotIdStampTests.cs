using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Transcendence.Data;
using Transcendence.Data.Models.LoL.Account;
using Transcendence.Data.Repositories.Implementations;
using Transcendence.Service.Core.Tests.Support;

namespace Transcendence.Service.Core.Tests;

// Production: renames and transfers leave stale rows that share a Riot ID. The ingestion producers picked
// the stale row, the refresh resolved the newest row by Riot ID and stamped only that one, so the stale
// row stayed the stalest candidate and was re-queued every tick (one player 14 times in an hour).
public sealed class SummonerRiotIdStampTests
{
    [Fact]
    public async Task MarkRiotIdRefreshedAsync_StampsEveryRowTheRiotIdResolvesToAndNothingElse()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<TranscendenceContext>().UseSqlite(connection).Options;
        await using var db = new SqliteCompatibleTranscendenceContext(options);
        await db.Database.EnsureCreatedAsync();

        var stale = new DateTime(2026, 2, 7, 0, 0, 0, DateTimeKind.Utc);
        Summoner Row(string gameName, string tagLine, bool normalized, string region = "NA1") => new()
        {
            Id = Guid.NewGuid(), Puuid = $"puuid-{Guid.NewGuid():N}", PlatformRegion = region, Region = "americas",
            GameName = gameName, TagLine = tagLine,
            GameNameNormalized = normalized ? gameName.ToUpperInvariant() : null,
            TagLineNormalized = normalized ? tagLine.ToUpperInvariant() : null,
            UpdatedAt = stale
        };
        var current = Row("DZe", "zzz", normalized: true);
        var caseVariant = Row("dze", "ZZZ", normalized: true);
        var legacy = Row("DZe", "zzz", normalized: false);
        var otherPlayer = Row("DZe", "yyy", normalized: true);
        var otherRegion = Row("DZe", "zzz", normalized: true, region: "EUW1");
        db.Summoners.AddRange(current, caseVariant, legacy, otherPlayer, otherRegion);
        await db.SaveChangesAsync();

        var refreshedAt = new DateTime(2026, 10, 22, 18, 0, 0, DateTimeKind.Utc);
        await new SummonerRepository(db, new RankRepository(db))
            .MarkRiotIdRefreshedAsync("NA1", "DZe", "zzz", refreshedAt);

        var stamped = await db.Summoners.AsNoTracking()
            .Where(s => s.UpdatedAt == refreshedAt)
            .Select(s => s.Id)
            .ToListAsync();
        stamped.Should().BeEquivalentTo([current.Id, caseVariant.Id, legacy.Id]);
    }
}
