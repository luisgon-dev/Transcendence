using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Transcendence.Data;
using Transcendence.Data.Extensions;
using Transcendence.Service.Core.Services.Extensions;
using Transcendence.Service.Core.Services.Jobs.Configuration;
using Transcendence.Service.Core.Services.LiveGame.Interfaces;
using Transcendence.Service.Core.Services.Tft.Configuration;
using Transcendence.Service.Core.Services.Tft.Interfaces;
using Transcendence.Service.Core.Tests.Support;

namespace Transcendence.Service.Core.Tests;

public class ServiceCollectionExtensionsTests
{
    [Fact]
    public async Task AddTranscendenceCore_BuildsWithoutRiotRegistrations()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpClient();
        services.AddDistributedMemoryCache();
        services.AddHybridCache();
        services.AddSingleton<IConfiguration>(
            new ConfigurationBuilder().AddInMemoryCollection().Build());
        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment());
        var dbOptions = new DbContextOptionsBuilder<TranscendenceContext>()
            .UseSqlite(connection)
            .Options;
        services.AddScoped<TranscendenceContext>(_ => new SqliteCompatibleTranscendenceContext(dbOptions));
        services.Configure<MultiRegionIngestionOptions>(_ => { });
        services.Configure<TftAnalyticsComputeOptions>(_ => { });
        services.AddProjectSyndraRepositories();
        services.AddTranscendenceCore();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });

        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITftAnalyticsService>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<ITftStaticDataService>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<ILiveGameService>().Should().NotBeNull();
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "Transcendence.Service.Core.Tests";
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
