using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Transcendence.Data;
using Transcendence.Data.Models.LoL.Account;

namespace Transcendence.IntegrationTests;

[Collection(PostgresIntegrationCollection.Name)]
public sealed class SummonerConcurrencyTests(PostgresIntegrationFixture fixture)
{
    private TranscendenceContext CreateContext() =>
        new(new DbContextOptionsBuilder<TranscendenceContext>()
            .UseNpgsql(fixture.ConnectionString)
            .Options);

    [Fact]
    public async Task ConcurrentProfileUpdates_FailFastThroughXmin()
    {
        var id = await SeedSummonerAsync();
        await using var firstContext = CreateContext();
        await using var secondContext = CreateContext();
        var first = await firstContext.Summoners.SingleAsync(x => x.Id == id);
        var second = await secondContext.Summoners.SingleAsync(x => x.Id == id);

        first.GameName = "First Writer";
        await firstContext.SaveChangesAsync();
        second.GameName = "Stale Writer";

        var act = () => secondContext.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateConcurrencyException>();
    }

    [Fact]
    public async Task ConcurrentLastActiveUpdates_MergeToMonotonicMaximum()
    {
        var id = await SeedSummonerAsync();
        await using var firstContext = CreateContext();
        await using var secondContext = CreateContext();
        var first = await firstContext.Summoners.SingleAsync(x => x.Id == id);
        var second = await secondContext.Summoners.SingleAsync(x => x.Id == id);
        first.Version.Should().NotBe(0, "the uint row-version is populated from PostgreSQL xmin");
        // PostgreSQL timestamp columns persist microseconds while DateTime can carry 100 ns ticks.
        // Align the fixture values to the provider's real precision so this remains an ordering test,
        // not a platform-dependent sub-microsecond serialization assertion.
        var now = TruncateToPostgresPrecision(DateTime.UtcNow);
        var earlier = now.AddMinutes(-2);
        var later = now.AddMinutes(-1);

        first.LastActiveAtUtc = earlier;
        second.LastActiveAtUtc = later;
        await firstContext.SaveChangesAsync();
        await secondContext.SaveChangesAsync();

        await using var verificationContext = CreateContext();
        var persisted = await verificationContext.Summoners.AsNoTracking().SingleAsync(x => x.Id == id);
        persisted.LastActiveAtUtc.Should().Be(later);
    }

    private static DateTime TruncateToPostgresPrecision(DateTime value) =>
        new(value.Ticks - value.Ticks % 10, value.Kind);

    private async Task<Guid> SeedSummonerAsync()
    {
        await using var context = CreateContext();
        var summoner = new Summoner
        {
            Id = Guid.NewGuid(),
            Puuid = $"concurrency-{Guid.NewGuid():N}",
            PlatformRegion = "NA1",
            Region = "AMERICAS",
            GameName = "Concurrency",
            TagLine = "NA1",
            UpdatedAt = DateTime.UtcNow
        };
        context.Summoners.Add(summoner);
        await context.SaveChangesAsync();
        return summoner.Id;
    }
}
