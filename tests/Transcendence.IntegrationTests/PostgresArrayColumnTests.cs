using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Transcendence.Data;
using Transcendence.Data.Models.LoL.Static;

namespace Transcendence.IntegrationTests;

/// <summary>
/// Proves the <c>List&lt;int&gt;</c> / <c>List&lt;string&gt;</c> columns on <see cref="ItemVersion"/>
/// round-trip through real Postgres <c>integer[]</c> / <c>text[]</c> with element order preserved. The
/// unit suite can't cover this: SQLite has no array type, so <c>SqliteCompatibleTranscendenceContext</c>
/// rewrites the model's array default away — meaning the real array mapping was previously unexercised.
/// </summary>
[Collection(PostgresIntegrationCollection.Name)]
public sealed class PostgresArrayColumnTests(PostgresIntegrationFixture fixture)
{
    private TranscendenceContext NewDb() =>
        new(new DbContextOptionsBuilder<TranscendenceContext>()
            .UseNpgsql(fixture.ConnectionString)
            .Options);

    [Fact]
    public async Task ItemVersion_IntAndStringArrays_RoundTripOnPostgres_WithOrderPreserved()
    {
        var version = $"arr-{Guid.NewGuid():N}"[..12];
        await using (var db = NewDb())
        {
            db.Patches.Add(new Patch { Version = version, ReleaseDate = DateTime.UtcNow, IsActive = false });
            db.ItemVersions.Add(new ItemVersion
            {
                ItemId = 3153,
                PatchVersion = version,
                Name = "Blade of the Ruined King",
                Tags = ["Damage", "LifeSteal", "AttackSpeed"],
                BuildsFrom = [1043, 1036, 1042],   // deliberately non-sorted to prove order is preserved
                BuildsInto = [],
                PriceTotal = 3200
            });
            await db.SaveChangesAsync();
        }

        await using var read = NewDb();
        var item = await read.ItemVersions.AsNoTracking()
            .SingleAsync(i => i.PatchVersion == version && i.ItemId == 3153);

        item.BuildsFrom.Should().Equal(1043, 1036, 1042);
        item.Tags.Should().Equal("Damage", "LifeSteal", "AttackSpeed");
        item.BuildsInto.Should().BeEmpty();
    }
}
