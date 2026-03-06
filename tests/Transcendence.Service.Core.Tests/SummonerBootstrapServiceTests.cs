using Camille.RiotGames;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Transcendence.Data;
using Transcendence.Data.Extensions;
using Transcendence.Data.Models.LoL.Account;
using Transcendence.Data.Models.LoL.Static;
using Transcendence.Service.Core.Services.Jobs;
using Transcendence.Service.Core.Services.Jobs.Configuration;

namespace Transcendence.Service.Core.Tests;

public class SummonerBootstrapServiceTests
{
    [Fact]
    public async Task EnsureSeededFromChallengerAsync_WhenMultiRegionAlreadySeeded_DoesNotThrowOnConcurrentRegions()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var dbOptions = new DbContextOptionsBuilder<TranscendenceContext>()
            .UseSqlite(connection)
            .Options;

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<TranscendenceContext>(_ => new TestSqliteTranscendenceContext(dbOptions));
        services.AddProjectSyndraRepositories();

        await using var provider = services.BuildServiceProvider();

        await using (var setupScope = provider.CreateAsyncScope())
        {
            var db = setupScope.ServiceProvider.GetRequiredService<TranscendenceContext>();
            await db.Database.EnsureCreatedAsync();

            db.Summoners.AddRange(
                BuildSummoner("NA1", "SeedNa"),
                BuildSummoner("EUW1", "SeedEuw"));
            await db.SaveChangesAsync();
        }

        var service = new SummonerBootstrapService(
            RiotGamesApi.NewInstance("test-api-key"),
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new SummonerBootstrapOptions
            {
                Enabled = true,
                PlatformRegion = "NA1",
                ChallengerSeedCount = 1,
                LockMinutes = 5
            }),
            Options.Create(new MultiRegionIngestionOptions
            {
                Enabled = true,
                MaxConcurrentRegionBootstraps = 2,
                Regions =
                [
                    new() { Region = "NA1", Enabled = true, ChallengerSeedCount = 1 },
                    new() { Region = "EUW1", Enabled = true, ChallengerSeedCount = 1 }
                ]
            }),
            provider.GetRequiredService<ILogger<SummonerBootstrapService>>());

        var seeded = await service.EnsureSeededFromChallengerAsync(CancellationToken.None);

        seeded.Should().Be(0);
    }

    private static Summoner BuildSummoner(string platformRegion, string gameName)
    {
        return new Summoner
        {
            Id = Guid.NewGuid(),
            RiotSummonerId = $"{platformRegion}-summoner-id",
            SummonerName = $"{gameName}#{platformRegion}",
            Puuid = $"{platformRegion}-puuid-{Guid.NewGuid():N}",
            GameName = gameName,
            TagLine = platformRegion,
            GameNameNormalized = gameName.ToUpperInvariant(),
            TagLineNormalized = platformRegion.ToUpperInvariant(),
            AccountId = $"{platformRegion}-account-id",
            PlatformRegion = platformRegion,
            Region = platformRegion is "EUW1" or "EUN1" ? "europe" : "americas",
            UpdatedAt = DateTime.UtcNow,
            SummonerLevel = 100,
            Ranks = []
        };
    }

    private sealed class TestSqliteTranscendenceContext(DbContextOptions<TranscendenceContext> options)
        : TranscendenceContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<ItemVersion>()
                .Property(x => x.BuildsFrom)
                .HasDefaultValueSql("'[]'");
            modelBuilder.Entity<ItemVersion>()
                .Property(x => x.BuildsInto)
                .HasDefaultValueSql("'[]'");
        }
    }
}
