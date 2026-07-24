using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration.Json;
using Transcendence.Data;
using Transcendence.Data.Extensions;
using Transcendence.Service.Core.Services.Analytics.Models;
using Transcendence.Service.Core.Services.Database;
using Transcendence.Service.Core.Services.Diagnostics;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using Transcendence.Service.Core.Services.Extensions;
using Transcendence.Service.Core.Services.Jobs;
using Transcendence.Service.Core.Services.Jobs.Configuration;
using Transcendence.Service.Core.Services.Jobs.Priority;
using Transcendence.Service.Workers;
using Transcendence.Service.Workers.Startup;

// Pre-warm the thread pool. This worker runs ~46 Hangfire workers of async I/O-bound jobs whose
// continuations, CancellationTokenSource.CancelAfter timers, and the per-region rate-limiter refill
// timers (Camille's and our RiotRateGate) all need pool threads. The default min pool (= CPU count) grows
// only ~1 thread/sec, so a burst of jobs starves it — timer callbacks don't fire, the rate limiters never
// refill, and consumers park forever on an empty token bucket while CPU sits idle (the ingestion stall).
// A high floor keeps timers responsive so the limiters always replenish.
ThreadPool.SetMinThreads(200, 200);

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
        .UsePostgreSqlStorage(
            options => options.UseNpgsqlConnection(
                builder.Configuration.GetConnectionString("MainDatabase")),
            new PostgreSqlStorageOptions
            {
                // Long analytics jobs can exceed the provider's 30-minute invisibility window.
                // Renew fetched-at while work is alive so another worker cannot steal and cancel it.
                UseSlidingInvisibilityTimeout = true
            }));
builder.Services.AddHangfireServer(options =>
{
    options.Queues = ["refresh-high", "default", "refresh-low"];
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

// Full player-history backfills are user-initiated but potentially long-running. Keep them off the
// quick refresh lane so a selected profile can go deep without delaying normal profile updates.
builder.Services.AddHangfireServer(options =>
{
    options.ServerName = HangfireQueues.HistoryBackfill;
    options.Queues = [HangfireQueues.HistoryBackfill];
    options.WorkerCount = 2;
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
    // Raise from the ~1 MiB default so large match-timeline DTOs still serialize to L2 (Redis)
    // instead of being silently dropped, which re-runs DB-bound compute on every cold read.
    options.MaximumPayloadBytes = 8 * 1024 * 1024; // 8 MiB
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
builder.Services.Configure<FullHistoryBackfillJobOptions>(builder.Configuration.GetSection("Jobs:FullHistoryBackfill"));
builder.Services.Configure<MatchFetchOptions>(builder.Configuration.GetSection("Jobs:MatchFetch"));
builder.Services.Configure<PatchPromotionOptions>(builder.Configuration.GetSection("Jobs:PatchPromotion"));
builder.Services.Configure<RiotRateGateOptions>(builder.Configuration.GetSection("Jobs:RiotRateGate"));
builder.Services.Configure<TimelineIngestionOptions>(builder.Configuration.GetSection("Jobs:TimelineIngestion"));
builder.Services.Configure<RuneSelectionIntegrityBackfillJobOptions>(
    builder.Configuration.GetSection("Jobs:RuneSelectionIntegrityBackfill"));
builder.Services.Configure<SummonerBootstrapOptions>(builder.Configuration.GetSection("Jobs:SummonerBootstrap"));
builder.Services.Configure<MultiRegionIngestionOptions>(builder.Configuration.GetSection("Jobs:MultiRegionIngestion"));
builder.Services.Configure<ProRosterDiscoveryOptions>(builder.Configuration.GetSection("Jobs:ProRosterDiscovery"));
builder.Services.Configure<ChampionAnalyticsComputeOptions>(builder.Configuration.GetSection("Analytics:Compute"));
builder.Services.Configure<TieringOptions>(builder.Configuration.GetSection("Analytics:Tiering"));
builder.Services.Configure<PrecomputedAnalyticsOptions>(
    builder.Configuration.GetSection("Analytics:Precompute"));
builder.Services.Configure<BuildResourceSnapshotOptions>(
    builder.Configuration.GetSection("Analytics:BuildAtlas"));
builder.Services.AddSingleton<IWorkerRecurringJobPolicy, WorkerRecurringJobPolicy>();
builder.Services.AddSingleton<WorkerStartupIntegrityState>();
builder.Services.AddSingleton<IWorkerStartupIntegrityService, WorkerStartupIntegrityService>();
builder.Services.AddSingleton<IStartupPatchRolloverService, StartupPatchRolloverService>();
builder.Services.AddSingleton<IAdaptiveThroughputBudgetPolicy, AdaptiveThroughputBudgetPolicy>();
builder.Services.AddSingleton<IStarvationGuardrailPolicy, StarvationGuardrailPolicy>();

// Worker liveness: producers beat IWorkerHeartbeat each dispatcher tick; a stale beat requests a
// bounded graceful host stop before the watchdog's hard-exit fallback lets restart:unless-stopped
// recreate a truly hung worker. Disable with Worker__Watchdog__Enabled=false.
builder.Services.Configure<WorkerWatchdogOptions>(builder.Configuration.GetSection("Worker:Watchdog"));
builder.Services.AddSingleton<IWorkerHeartbeat, WorkerHeartbeat>();
builder.Services.AddHostedService<WorkerWatchdogService>();

// Poll-based ingestion-health alerts (failed-job spike / stuck discovery backlog / throughput
// stall). Posts to Alerts:Webhook:Url; logs only when unset, so this ships without a secret.
builder.Services.Configure<AlertOptions>(builder.Configuration.GetSection("Alerts"));
builder.Services.AddSingleton<IAlertNotifier, WebhookAlertNotifier>();

// Worker metrics → Prometheus via a standalone HttpListener on :9464 (the worker has no HTTP server).
// Exports the ingestion-throughput + refresh-lock meters, the rate-gate telemetry, plus .NET runtime
// (GC / thread pool), outbound HTTP (Riot API), and HybridCache instrumentation. Gated so a
// listener/binding problem can be disabled via env (Telemetry__Enabled=false) without a rollback.
if (builder.Configuration.GetValue("Telemetry:Enabled", true))
{
    var metricsPort = builder.Configuration.GetValue("Telemetry:PrometheusPort", 9464);
    builder.Services.AddSingleton<Transcendence.Service.Core.Services.RiotApi.RiotRateGateTelemetry>();
    builder.Services.AddOpenTelemetry()
        .ConfigureResource(resource => resource.AddService("transcendence-worker"))
        .WithMetrics(metrics => metrics
            .AddMeter("Transcendence.IngestionThroughput")
            .AddMeter("Transcendence.RefreshLocks")
            .AddMeter(Transcendence.Service.Core.Services.RiotApi.RiotRateGateTelemetry.MeterName)
            .AddMeter("Microsoft.Extensions.Caching.Hybrid")
            .AddRuntimeInstrumentation()
            .AddHttpClientInstrumentation()
            .AddPrometheusHttpListener(options =>
            {
                // The prerelease Host/Port replacement cannot express HttpListener's strong
                // wildcard: UriBuilder rejects "+" and "0.0.0.0" is not a supported prefix.
                // Keep the transitional property until the exporter provides a wildcard-safe API.
#pragma warning disable CS0618 // UriPrefixes is required for wildcard container-network binding.
                options.UriPrefixes = [$"http://+:{metricsPort}/"];
#pragma warning restore CS0618
            }));
}

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

// add data repositories
builder.Services.AddProjectSyndraRepositories();

var host = builder.Build();

// Apply pending EF migrations before the worker starts (gated by Database:AutoMigrate). EF Core's migration
// lock makes this safe even though the WebAPI host runs the same step on a simultaneous deploy.
await DatabaseMigrator.MigrateIfEnabledAsync(
    host.Services,
    builder.Configuration.GetValue("Database:AutoMigrate", false),
    host.Services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DatabaseMigrator)));

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
