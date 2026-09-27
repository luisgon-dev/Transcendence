using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Transcendence.Data;
using Transcendence.Data.Models.LoL.Account;
using Transcendence.Data.Repositories.Implementations;

namespace Transcendence.IntegrationTests;

/// <summary>
/// The Riot ID lookup's two fallbacks for legacy rows (no normalized key) are now restricted to those
/// rows so the partial indexes can serve them. They must still find a legacy row by exact name and by
/// a different letter case, and a row written with the key must still be found by the fast path.
/// </summary>
[Collection(PostgresIntegrationCollection.Name)]
public sealed class SummonerRiotIdLookupTests(PostgresIntegrationFixture fixture)
{
    [Fact]
    public async Task FindByRiotId_FindsLegacyRowsByExactNameAndByAnyCase()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        await using (var db = CreateContext())
        {
            db.Summoners.Add(Legacy($"Legacy{suffix}", "EUW"));
            db.Summoners.Add(new Summoner
            {
                Id = Guid.NewGuid(),
                Puuid = $"normalized-{suffix}",
                PlatformRegion = "EUW1",
                Region = "EUROPE",
                GameName = $"Current{suffix}",
                TagLine = "EUW",
                GameNameNormalized = $"CURRENT{suffix}".ToUpperInvariant(),
                TagLineNormalized = "EUW",
                UpdatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        await using var context = CreateContext();
        var repository = new SummonerRepository(context, new RankRepository(context));

        (await repository.FindByRiotIdAsync("EUW1", $"Legacy{suffix}", "EUW"))?.Puuid
            .Should().Be($"legacy-{suffix}", "the exact-name fallback serves a legacy row");
        (await repository.FindByRiotIdAsync("EUW1", $"legacy{suffix}".ToLowerInvariant(), "euw"))?.Puuid
            .Should().Be($"legacy-{suffix}", "the case-insensitive fallback serves a legacy row");
        (await repository.FindByRiotIdAsync("EUW1", $"current{suffix}", "euw"))?.Puuid
            .Should().Be($"normalized-{suffix}");
        (await repository.FindByRiotIdAsync("EUW1", $"Nobody{suffix}", "EUW")).Should().BeNull();
    }

    private Summoner Legacy(string gameName, string tagLine) => new()
    {
        Id = Guid.NewGuid(),
        Puuid = $"legacy-{gameName[6..]}",
        PlatformRegion = "EUW1",
        Region = "EUROPE",
        GameName = gameName,
        TagLine = tagLine,
        UpdatedAt = DateTime.UtcNow
    };

    private TranscendenceContext CreateContext() =>
        new(new DbContextOptionsBuilder<TranscendenceContext>().UseNpgsql(fixture.ConnectionString).Options);
}
