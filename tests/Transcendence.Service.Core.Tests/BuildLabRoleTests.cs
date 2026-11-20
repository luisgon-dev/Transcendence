using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Transcendence.Data;
using Transcendence.Data.Models.LoL.Analytics;
using Transcendence.Data.Models.LoL.Static;
using Transcendence.Service.Core.Services.Analytics.Implementations;
using Transcendence.Service.Core.Services.Analytics.Models;
using Transcendence.Service.Core.Tests.Support;

namespace Transcendence.Service.Core.Tests;

public class BuildLabRoleTests
{
    [Fact]
    public async Task AutoRoleRespectsRegionAndPatchWhileAnExplicitLaneTakesPriority()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<TranscendenceContext>().UseSqlite(connection).Options;
        await using var db = new SqliteCompatibleTranscendenceContext(options);
        await db.Database.EnsureCreatedAsync();

        const string patch = "16.14";
        db.Patches.Add(new Patch { Version = patch, ReleaseDate = DateTime.UtcNow, IsActive = true });
        db.BuildLabCoverage.Add(new BuildLabCoverage { Patch = patch, Region = "ALL", Matches = 500 });
        db.ChampionRoleTierStats.AddRange(
            RoleGames(patch, "KR", "UTILITY", 20),
            RoleGames(patch, "NA1", "JUNGLE", 120),
            RoleGames(patch, "KR", "JUNGLE", 10),
            RoleGames("16.13", "KR", "MIDDLE", 400));
        await db.SaveChangesAsync();

        var services = new ServiceCollection().AddLogging();
        services.AddHybridCache();
        await using var provider = services.BuildServiceProvider();
        var service = new BuildLabService(db, provider.GetRequiredService<HybridCache>(),
            Options.Create(new BuildLabOptions { Enabled = true }));
        var query = new BuildLabQuery(103, null, null, null, "KR", "items", "supported", [], []);

        (await service.GetAsync(query)).Context.Role.Should().Be("UTILITY");
        (await service.GetAsync(query with { Region = "EUW1" })).Context.Role.Should().Be("JUNGLE");
        (await service.GetAsync(query with { Patch = "16.13" })).Context.Role.Should().Be("MIDDLE");
        (await service.GetAsync(query with { Role = "top" })).Context.Role.Should().Be("TOP");
        var missing = await service.GetAsync(query with { ChampionId = 64 });
        missing.Context.Role.Should().BeNull();
        missing.Available.Should().BeFalse();
    }

    private static ChampionRoleTierStat RoleGames(string patch, string region, string role, int games) => new()
    {
        Patch = patch, PlatformRegion = region, ChampionId = 103, Role = role, RankTier = "DIAMOND", Games = games
    };
}
