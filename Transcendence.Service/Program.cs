using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration.Json;
using Transcendence.Data;
using Transcendence.Data.Extensions;
using Transcendence.Service.Core.Services.Analytics.Models;
using Transcendence.Service.Core.Services.Diagnostics;
using Transcendence.Service.Core.Services.Extensions;
using Transcendence.Service.Core.Services.Jobs;
using Transcendence.Service.Core.Services.Jobs.Configuration;
using Transcendence.Service.Core.Services.Jobs.Priority;
using Transcendence.Service.Core.Services.Tft.Configuration;
using Transcendence.Service.Workers;
using Transcendence.Service.Workers.Startup;

var builder = Host.CreateApplicationBuilder(args);
ConfigureSharedBackendConfiguration(builder.Configuration, builder.Environment);
builder.Logging.AddOperationalFileLogger(builder.Configuration, defaultServiceName: "service");

// Add services to the container.
builder.Services.AddDbContextPool<TranscendenceContext>(options =>
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
    options.Queues = ["refresh-high", "default", "refresh-low", "tft-refresh-high", "tft-default", "tft-refresh-low"];
    // Refresh jobs are I/O-bound (awaiting the Riot API, throttled per-region by Camille), so a
    // worker count well above CPU count keeps more regions/summoners in flight concurrently.
    options.WorkerCount = 24;
});

// Dedicated worker pool for the analytics warm/refresh lane. The main pool above pulls queues
// highest-priority-first, so a saturated refresh backlog could starve a low-priority analytics
// job indefinitely. A second server with its own workers — serving ONLY the reserved queue —
// guarantees these jobs are always ready to run on schedule no matter how busy the main pool is.
builder.Services.AddHangfireServer(options =>
{
    options.ServerName = HangfireQueues.AnalyticsWarm;
    options.Queues = [HangfireQueues.AnalyticsWarm];
    // Sized for the lane's recurring jobs (default-profile warm + adaptive + ramp refresh) running
    // concurrently; each fans out internally, so a small dedicated pool is plenty.
    options.WorkerCount = 4;
});

// Dedicated worker pool for per-match timeline ingestion. The main pool's shared refresh-low queue
// carries a very large background backlog (champion-analytics ingestion, summoner maintenance, …),
// so timeline jobs sharing it get starved FIFO. A reserved lane with its own bounded pool drains the
// re-ingestion backlog steadily at the Riot rate limit without competing with — or starving — that
// backlog. Bounded (not the main 24) so it never monopolises Riot capacity from user-driven refreshes.
builder.Services.AddHangfireServer(options =>
{
    options.ServerName = HangfireQueues.TimelineIngest;
    options.Queues = [HangfireQueues.TimelineIngest];
    options.WorkerCount = 8;
});

// Dedicated worker pool for match DISCOVERY (the heaviest pipeline): the per-region champion-analytics
// ingestion / summoner-maintenance producers and the analytics summoner-refresh consumers they enqueue.
// Previously these shared refresh-low and got buried under the broad maintenance backlog, so the
// producers couldn't even run to enqueue the refreshes that fetch new current-patch matches. A reserved
// lane guarantees discovery always has workers; bounded so it shares Riot capacity fairly with the rest.
builder.Services.AddHangfireServer(options =>
{
    options.ServerName = HangfireQueues.Discovery;
    options.Queues = [HangfireQueues.Discovery];
    // Discovery consumers are I/O-bound on the Riot API. Concurrency here = concurrent Riot requests,
    // which is capped by the prod key's rate limit, NOT by CPU/connections. Setting this too high makes
    // consumers generate requests faster than the key allows, so Camille's rate limiter parks them and
    // every Riot-calling job stalls (observed outage). Keep it modest so the steady request rate fits
    // the key; raise only if the key's tier genuinely supports more sustained throughput.
    options.WorkerCount = 8;
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
builder.Services.Configure<WorkerSchedulingProfileOptions>(builder.Configuration.GetSection("Jobs:SchedulingProfiles"));
builder.Services.Configure<LiveGamePollingJobOptions>(builder.Configuration.GetSection("Jobs:LiveGamePolling"));
builder.Services.Configure<RetryFailedMatchesJobOptions>(builder.Configuration.GetSection("Jobs:RetryFailedMatches"));
builder.Services.Configure<RefreshChampionAnalyticsJobOptions>(
    builder.Configuration.GetSection("Jobs:RefreshChampionAnalytics"));
builder.Services.Configure<WarmDefaultChampionProfilesJobOptions>(
    builder.Configuration.GetSection("Jobs:WarmDefaultChampionProfiles"));
builder.Services.Configure<ChampionAnalyticsIngestionJobOptions>(
    builder.Configuration.GetSection("Jobs:ChampionAnalyticsIngestion"));
builder.Services.Configure<SummonerMaintenanceJobOptions>(builder.Configuration.GetSection("Jobs:SummonerMaintenance"));
builder.Services.Configure<AdaptiveThroughputBudgetOptions>(
    builder.Configuration.GetSection("Jobs:AdaptiveThroughputBudget"));
builder.Services.Configure<StarvationGuardrailOptions>(
    builder.Configuration.GetSection("Jobs:StarvationGuardrail"));
builder.Services.Configure<IngestionPriorityPolicyOptions>(
    builder.Configuration.GetSection("Jobs:IngestionPriorityPolicy"));
builder.Services.Configure<MatchIngestionOptions>(builder.Configuration.GetSection("Jobs:MatchIngestion"));
builder.Services.Configure<PatchPromotionOptions>(builder.Configuration.GetSection("Jobs:PatchPromotion"));
builder.Services.Configure<TimelineIngestionOptions>(builder.Configuration.GetSection("Jobs:TimelineIngestion"));
builder.Services.Configure<RuneSelectionIntegrityBackfillJobOptions>(
    builder.Configuration.GetSection("Jobs:RuneSelectionIntegrityBackfill"));
builder.Services.Configure<SummonerBootstrapOptions>(builder.Configuration.GetSection("Jobs:SummonerBootstrap"));
builder.Services.Configure<MultiRegionIngestionOptions>(builder.Configuration.GetSection("Jobs:MultiRegionIngestion"));
builder.Services.Configure<ChampionAnalyticsComputeOptions>(builder.Configuration.GetSection("Analytics:Compute"));
builder.Services.AddSingleton<IWorkerRecurringJobPolicy, WorkerRecurringJobPolicy>();
builder.Services.AddSingleton<WorkerStartupIntegrityState>();
builder.Services.AddSingleton<IWorkerStartupIntegrityService, WorkerStartupIntegrityService>();
builder.Services.AddSingleton<IStartupPatchRolloverService, StartupPatchRolloverService>();
builder.Services.AddSingleton<IAdaptiveThroughputBudgetPolicy, AdaptiveThroughputBudgetPolicy>();
builder.Services.AddSingleton<IStarvationGuardrailPolicy, StarvationGuardrailPolicy>();

// worker that initiates services
if (builder.Environment.IsDevelopment())
    // development worker directly enqueues and cleans up jobs for development
    builder.Services.AddHostedService<DevelopmentWorker>();
else
    builder.Services.AddHostedService<ProductionWorker>();

// Register services
builder.Services.AddTranscendenceCore();
builder.Services.AddTranscendenceWorkerCore();
builder.Services.AddTranscendenceLeagueRiot(builder.Configuration);
builder.Services.AddTranscendenceTftRiot(builder.Configuration);
builder.Services.Configure<TftAnalyticsComputeOptions>(builder.Configuration.GetSection("Analytics:TftCompute"));

// add data repositories
builder.Services.AddProjectSyndraRepositories();

var host = builder.Build();
host.Run();

static void ConfigureSharedBackendConfiguration(ConfigurationManager configuration, IHostEnvironment environment)
{
    var retainedSources = configuration.Sources
        .Where(source => source is not JsonConfigurationSource jsonSource || !IsHostAppsettingsSource(jsonSource, environment))
        .ToList();

    configuration.Sources.Clear();
    configuration.AddJsonFile(GetSharedConfigPath(environment), optional: false, reloadOnChange: environment.IsDevelopment());
    configuration.AddJsonFile("appsettings.json", optional: false, reloadOnChange: environment.IsDevelopment());

    foreach (var source in retainedSources)
        configuration.Sources.Add(source);
}

static bool IsHostAppsettingsSource(JsonConfigurationSource jsonSource, IHostEnvironment environment)
{
    if (string.IsNullOrWhiteSpace(jsonSource.Path))
        return false;

    var normalizedPath = jsonSource.Path.Replace('\\', '/');
    return normalizedPath.Equals("appsettings.json", StringComparison.OrdinalIgnoreCase)
           || normalizedPath.Equals($"appsettings.{environment.EnvironmentName}.json", StringComparison.OrdinalIgnoreCase);
}

static string GetSharedConfigPath(IHostEnvironment environment)
{
    var outputPath = Path.Combine(environment.ContentRootPath, "config", "backend.shared.json");
    if (File.Exists(outputPath))
        return outputPath;

    return Path.GetFullPath(Path.Combine(environment.ContentRootPath, "..", "config", "backend.shared.json"));
}
