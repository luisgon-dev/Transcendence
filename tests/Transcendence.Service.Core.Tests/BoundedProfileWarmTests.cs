using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Transcendence.Data;
using Transcendence.Data.Models.LoL.Account;
using Transcendence.Data.Models.LoL.Match;
using Transcendence.Data.Models.LoL.Static;
using Transcendence.Service.Core.Services.Analytics.Interfaces;
using Transcendence.Service.Core.Services.Jobs;
using Transcendence.Service.Core.Services.Jobs.Configuration;
using Transcendence.Service.Core.Tests.Support;

namespace Transcendence.Service.Core.Tests;

public sealed class BoundedProfileWarmTests
{
    [Fact]
    public async Task ShortRuns_RotatePersistedCoverage_AndReuseTheHourlyCandidateRoster()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<TranscendenceContext>().UseSqlite(connection).Options;
        var analytics = new Mock<IChampionAnalyticsService>();
        analytics.Setup(service => service.RefreshDefaultProfileCacheAsync(
            It.IsAny<int>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHybridCache();
        services.AddScoped<TranscendenceContext>(_ => new SqliteCompatibleTranscendenceContext(options));
        services.AddSingleton(analytics.Object);
        await using var provider = services.BuildServiceProvider();
        using var root = provider.CreateScope();
        var db = root.ServiceProvider.GetRequiredService<TranscendenceContext>();
        await db.Database.EnsureCreatedAsync();
        db.Patches.Add(new Patch { Version = "16.19", IsActive = true, ReleaseDate = DateTime.UtcNow });
        var match = new Data.Models.LoL.Match.Match
        {
            Id = Guid.NewGuid(), MatchId = "NA1_WARM_TEST", Patch = "16.19", QueueId = 420, Status = FetchStatus.Success
        };
        db.Matches.Add(match);
        for (var id = 1; id <= 2; id++)
        {
            var summoner = new Summoner { Id = Guid.NewGuid(), Puuid = $"warm-{id}", PlatformRegion = "NA1", Region = "AMERICAS" };
            db.Summoners.Add(summoner);
            db.MatchParticipants.Add(new MatchParticipant
            {
                Id = Guid.NewGuid(), MatchId = match.Id, Match = match, SummonerId = summoner.Id, Summoner = summoner,
                ParticipantId = id, ChampionId = id, TeamPosition = "TOP", TeamId = 100
            });
        }
        await db.SaveChangesAsync();
        WarmDefaultChampionProfilesJob NewJob() => new(provider.GetRequiredService<IServiceScopeFactory>(), db,
            Options.Create(new WarmDefaultChampionProfilesJobOptions
            {
                MinimumGamesToWarm = 1, MaxChampionsPerRun = 1, MaxConcurrency = 1, IncludeProBuilds = false
            }), NullLogger<WarmDefaultChampionProfilesJob>.Instance, provider.GetRequiredService<HybridCache>());
        await NewJob().ExecuteAsync(CancellationToken.None);
        (await db.AnalyticsResponseSnapshots.CountAsync(row => row.Feature == "profile-warm")).Should().Be(1);
        // If the next short run rescans raw data, the candidate list is empty and it cannot rotate.
        // The one-hour eligibility snapshot must remain stable between ticks.
        await db.Matches.IgnoreQueryFilters().ExecuteDeleteAsync();
        await NewJob().ExecuteAsync(CancellationToken.None);
        (await db.AnalyticsResponseSnapshots.CountAsync(row => row.Feature == "profile-warm")).Should().Be(2);
        analytics.Invocations.Where(call => call.Method.Name == nameof(IChampionAnalyticsService.RefreshDefaultProfileCacheAsync))
            .Select(call => (int)call.Arguments[0]!).Should().OnlyHaveUniqueItems().And.HaveCount(2);
    }
}
