using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.EntityFrameworkCore;
using Transcendence.Data;
using Transcendence.Data.Extensions;
using Transcendence.Service.Core.Services.Analytics.Models;
using Transcendence.Service.Core.Services.Extensions;
using Transcendence.Service.Core.Services.Jobs.Configuration;
using Transcendence.Service.Workers;

var builder = Host.CreateApplicationBuilder(args);

// Add services to the container.
builder.Services.AddDbContext<TranscendenceContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("MainDatabase"),
        b => b.MigrationsAssembly("Transcendence.Service")));

var hangfireRetryAttempts = Math.Max(0, builder.Configuration.GetValue<int?>("Jobs:Hangfire:GlobalRetryAttempts") ?? 1);

builder.Services.AddHangfire(config =>
    config.SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
        .UseSimpleAssemblyNameTypeSerializer()
        .UseRecommendedSerializerSettings()
        .UseFilter(new AutomaticRetryAttribute
        {
            Attempts = hangfireRetryAttempts
        })
        .UsePostgreSqlStorage(options =>
            options.UseNpgsqlConnection(builder.Configuration.GetConnectionString("MainDatabase"))));
builder.Services.AddHangfireServer(options =>
{
    options.Queues = ["refresh-high", "default", "refresh-low"];
});

builder.Services.AddHttpClient();

// Configure Redis distributed cache
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = builder.Configuration.GetConnectionString("Redis");
    options.InstanceName = "Transcendence_";
});

// Configure HybridCache with L1/L2 TTL relationship
builder.Services.AddHybridCache(options =>
{
    options.DefaultEntryOptions = new Microsoft.Extensions.Caching.Hybrid.HybridCacheEntryOptions
    {
        Expiration = TimeSpan.FromHours(1),           // L2 Redis TTL
        LocalCacheExpiration = TimeSpan.FromMinutes(5) // L1 Memory TTL (shorter than L2)
    };
});

builder.Services.Configure<WorkerJobScheduleOptions>(builder.Configuration.GetSection("Jobs:Schedule"));
builder.Services.Configure<LiveGamePollingJobOptions>(builder.Configuration.GetSection("Jobs:LiveGamePolling"));
builder.Services.Configure<RetryFailedMatchesJobOptions>(builder.Configuration.GetSection("Jobs:RetryFailedMatches"));
builder.Services.Configure<RefreshChampionAnalyticsJobOptions>(
    builder.Configuration.GetSection("Jobs:RefreshChampionAnalytics"));
builder.Services.Configure<ChampionAnalyticsIngestionJobOptions>(
    builder.Configuration.GetSection("Jobs:ChampionAnalyticsIngestion"));
builder.Services.Configure<SummonerBootstrapOptions>(builder.Configuration.GetSection("Jobs:SummonerBootstrap"));
builder.Services.Configure<ChampionAnalyticsComputeOptions>(builder.Configuration.GetSection("Analytics:Compute"));

// worker that initiates services
if (builder.Environment.IsDevelopment())
    // development worker directly enqueues and cleans up jobs for development
    builder.Services.AddHostedService<DevelopmentWorker>();
else
    builder.Services.AddHostedService<ProductionWorker>();

// Register services
builder.Services.AddTranscendenceCore();
builder.Services.AddTranscendenceRiot(builder.Configuration);

// add data repositories
builder.Services.AddProjectSyndraRepositories();

var host = builder.Build();
host.Run();
