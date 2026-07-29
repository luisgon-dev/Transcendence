using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using StackExchange.Redis;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using System.Net;
using System.Net.Sockets;
using System.Threading.RateLimiting;
using System.Text;
using Transcendence.WebAPI.Health;
using Transcendence.Service.Core.Services.Auth.Implementations;
using Transcendence.Service.Core.Services.Auth.Interfaces;
using Transcendence.Service.Core.Services.Auth.Models;
using Transcendence.Service.Core.Services.Analytics.Models;
using Transcendence.Service.Core.Services.Diagnostics;
using Transcendence.Service.Core.Services.Extensions;
using Transcendence.Service.Core.Services.Jobs.Configuration;
using Transcendence.Service.Core.Services.Jobs.Priority;
using Transcendence.WebAPI.Errors;
using Transcendence.WebAPI.Security;

var builder = WebApplication.CreateBuilder(args);
ConfigureSharedBackendConfiguration(builder.Configuration, builder.Environment);
var isOpenApiExport = ParseBool(builder.Configuration["OpenApi:ExportOnly"], false);
builder.Logging.AddOperationalFileLogger(builder.Configuration, defaultServiceName: "webapi");
var requireJwtKeyInDevelopment = ParseBool(builder.Configuration["Auth:Jwt:RequireKeyInDevelopment"], false);
var bootstrapApiKey = builder.Configuration["Auth:BootstrapApiKey"];
var bootstrapApiKeyDevOnly = ParseBool(builder.Configuration["Auth:BootstrapApiKeyEnabledInDevelopmentOnly"], true);
if (bootstrapApiKeyDevOnly && !builder.Environment.IsDevelopment() && !string.IsNullOrWhiteSpace(bootstrapApiKey))
{
    throw new InvalidOperationException(
        "Auth:BootstrapApiKey is configured outside Development while Auth:BootstrapApiKeyEnabledInDevelopmentOnly=true.");
}

// Add services to the container.

builder.Services.AddControllers(options =>
    // Normalize body-carrying error responses (e.g. BadRequest("...")) to ProblemDetails — see
    // ProblemDetailsErrorBodyFilter. Empty-body 4xx/5xx + model validation are already ProblemDetails
    // via [ApiController]; unhandled exceptions via ApiExceptionHandler.
    options.Filters.Add<ProblemDetailsErrorBodyFilter>());
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    // Public read limiters are partitioned PER CLIENT IP (not one global window), so a single client
    // cannot exhaust everyone's budget. Internal/SSR traffic is exempt — the web frontend's server-side
    // fetches reach the API directly (no X-Forwarded-For → a private/loopback address), and would
    // otherwise all collapse into one shared partition. See BuildIpReadPartition.
    options.AddPolicy("expensive-read", httpContext => BuildIpReadPartition(httpContext, permitLimit: 120));
    options.AddPolicy("search-read", httpContext => BuildIpReadPartition(httpContext, permitLimit: 600));
    options.AddPolicy("multisearch-read", httpContext => BuildIpReadPartition(httpContext, permitLimit: 60));
    options.AddFixedWindowLimiter("admin-write", limiter =>
    {
        limiter.PermitLimit = 30;
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        limiter.QueueLimit = 0;
    });
    options.AddPolicy("auth-login", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: BuildAuthRateLimitPartitionKey(httpContext),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 8,
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            }));
    options.AddPolicy("auth-register", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: BuildAuthRateLimitPartitionKey(httpContext),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 4,
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            }));
    options.AddPolicy("auth-refresh", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: BuildAuthRateLimitPartitionKey(httpContext),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 20,
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            }));
});
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    // Emit C# nullable-reference-type semantics into the schema (spec stays OpenAPI 3.0): a non-nullable
    // reference property (e.g. `string Puuid`) is no longer marked nullable, and a nullable one (`RankInfo?`)
    // emits `allOf:[{$ref}], nullable:true`. NonNullableReferenceTypesAsRequired additionally lists
    // non-null reference properties in each schema's `required` set. Together the generated TS client types
    // always-present fields as non-optional/non-null and sometimes-null fields as `T | null`, fixing the
    // inverted nullability the client shipped before (P1 — API Design & Contracts).
    options.SupportNonNullableReferenceTypes();
    options.NonNullableReferenceTypesAsRequired();
    // Wrap $ref properties in `allOf` so sibling keywords apply — in OpenAPI 3.0 a bare `$ref` cannot
    // carry a `nullable: true` sibling, so without this a nullable object property (e.g. `RankInfo? SoloRank`)
    // would serialize as a bare `$ref` and the generated client would type it non-null. With allOf-wrapping
    // it becomes `{ allOf: [{$ref}], nullable: true }` → `RankInfo | null` in the TS client.
    options.UseAllOfToExtendReferenceSchemas();

    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Transcendence API",
        Version = "v1",
        Description = "League analytics API with app API-key and user JWT authentication."
    });

    options.AddSecurityDefinition(AuthPolicies.ApiKeyScheme, new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.ApiKey,
        In = ParameterLocation.Header,
        Name = "X-API-Key",
        Description = "App authentication key for operational endpoints."
    });

    options.AddSecurityDefinition(JwtBearerDefaults.AuthenticationScheme, new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Description = "User JWT access token from /api/auth/login."
    });

    options.OperationFilter<AuthPolicyOperationFilter>();
    options.OperationFilter<ProblemDetailsContentTypeOperationFilter>();
});

// Infrastructure: DbContext, HTTP, domain services, repositories.
// Non-pooled (not AddDbContextPool): pooled contexts can be reused by a second request before a
// cancelled query (e.g. a heavy analytics read that hit the command timeout under load) has fully
// unwound, surfacing "A second operation was started on this context instance". A fresh per-scope
// context is disposed at scope end and never reused, eliminating that race. The analytics reads are
// cache-backed so the lost pooling has negligible throughput impact.
builder.Services.AddTranscendenceData(builder.Configuration);
// Readiness checks (tagged "ready") back /health/ready; /health/live stays shallow
// (process-up only). Redis check is registered only when Redis is configured so the
// keyless OpenAPI export / api:check boot (no Redis) does not fail.
var healthChecks = builder.Services.AddHealthChecks()
    .AddCheck<DatabaseReadinessHealthCheck>("postgres", tags: new[] { "ready" });
if (!string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString("Redis")))
{
    healthChecks.AddCheck<RedisReadinessHealthCheck>("redis", tags: new[] { "ready" });
}

builder.Services.AddHttpClient();

// Metrics → Prometheus, scraped at /metrics. Exports ASP.NET request, outbound HTTP, .NET runtime
// (GC / thread pool), and HybridCache instrumentation for the read API.
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("transcendence-webapi"))
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddRuntimeInstrumentation()
        .AddMeter(LeaderboardTelemetry.MeterName)
        .AddMeter("Microsoft.Extensions.Caching.Hybrid")
        .AddPrometheusExporter());

// Configure Redis distributed cache
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = builder.Configuration.GetConnectionString("Redis");
    options.InstanceName = "Transcendence_";
});

// Shared Redis multiplexer — registered as a singleton and reused for both DataProtection
// key persistence and the readiness health check, so the host holds one connection rather
// than several. AbortOnConnectFail=false so the host still starts when Redis is briefly
// unreachable (CI/OpenAPI-export has no Redis; prod startup shouldn't hard-fail on a Redis
// blip). No-op when Redis isn't configured.
var dataProtectionRedis = builder.Configuration.GetConnectionString("Redis");
if (!string.IsNullOrWhiteSpace(dataProtectionRedis))
{
    var redisOptions = ConfigurationOptions.Parse(dataProtectionRedis);
    redisOptions.AbortOnConnectFail = false;
    var redisMultiplexer = ConnectionMultiplexer.Connect(redisOptions);
    builder.Services.AddSingleton<IConnectionMultiplexer>(redisMultiplexer);

    // Persist DataProtection keys to Redis so antiforgery/auth cookies survive container
    // redeploys (the default ephemeral key-ring is regenerated on every deploy, logging
    // users out and emitting a startup warning).
    builder.Services.AddDataProtection()
        .SetApplicationName("Transcendence")
        .PersistKeysToStackExchangeRedis(redisMultiplexer, "Transcendence:DataProtection:Keys");
}

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

// Register keyless application services used by the WebAPI host.
builder.Services.AddTranscendenceCore();
builder.Services.Configure<ChampionAnalyticsComputeOptions>(builder.Configuration.GetSection("Analytics:Compute"));
builder.Services.Configure<TieringOptions>(builder.Configuration.GetSection("Analytics:Tiering"));
builder.Services.Configure<BuildLabModelingOptions>(builder.Configuration.GetSection("Analytics:BuildLab"));
builder.Services.Configure<SavedBuildOptions>(builder.Configuration.GetSection("Analytics:SavedBuilds"));
builder.Services.Configure<ChampionAnalyticsIngestionJobOptions>(
    builder.Configuration.GetSection("Jobs:ChampionAnalyticsIngestion"));
builder.Services.Configure<MultiRegionIngestionOptions>(builder.Configuration.GetSection("Jobs:MultiRegionIngestion"));
builder.Services.Configure<WorkerJobScheduleOptions>(builder.Configuration.GetSection("Jobs:Schedule"));
builder.Services.Configure<WorkerSchedulingProfileOptions>(builder.Configuration.GetSection("Jobs:SchedulingProfiles"));
builder.Services.Configure<AdminBootstrapOptions>(builder.Configuration.GetSection("Auth:AdminBootstrap"));
builder.Services.Configure<PasswordResetOptions>(builder.Configuration.GetSection("Auth:PasswordReset"));
builder.Services.Configure<RiotRsoOptions>(builder.Configuration.GetSection("Auth:RiotRso"));
builder.Services.AddSingleton<IWorkerRecurringJobPolicy, WorkerRecurringJobPolicy>();
builder.Services.AddAdminOperationsFacades();
builder.Services.AddSingleton<IAdaptiveThroughputBudgetPolicy, AdaptiveThroughputBudgetPolicy>();
builder.Services.AddSingleton<IStarvationGuardrailPolicy, StarvationGuardrailPolicy>();

var jwtIssuer = builder.Configuration["Auth:Jwt:Issuer"] ?? "Transcendence";
var jwtAudience = builder.Configuration["Auth:Jwt:Audience"] ?? "TranscendenceClients";
var jwtKey = JwtService.ResolveSigningKey(
    builder.Configuration["Auth:Jwt:Key"],
    builder.Environment,
    requireJwtKeyInDevelopment);

builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = AuthPolicies.ApiKeyScheme;
        options.DefaultChallengeScheme = AuthPolicies.ApiKeyScheme;
    })
    .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(
        AuthPolicies.ApiKeyScheme,
        _ => { })
    .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1),
            ValidIssuer = jwtIssuer,
            ValidAudience = jwtAudience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(AuthPolicies.AppOnly, policy =>
        policy.AddAuthenticationSchemes(AuthPolicies.ApiKeyScheme)
            .RequireAuthenticatedUser());

    options.AddPolicy(AuthPolicies.UserOnly, policy =>
        policy.AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme)
            .RequireAuthenticatedUser());

    options.AddPolicy(AuthPolicies.AppOrUser, policy =>
        policy.AddAuthenticationSchemes(AuthPolicies.ApiKeyScheme, JwtBearerDefaults.AuthenticationScheme)
            .RequireAuthenticatedUser());

    options.AddPolicy(AuthPolicies.AdminOnly, policy =>
        policy.AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme)
            .RequireAuthenticatedUser()
            .RequireRole(SystemRoles.Admin));
});

// Configure Hangfire client (no server) for enqueueing jobs. Swagger export does not
// resolve controllers or enqueue work, so avoid initializing PostgreSQL-backed job storage.
if (!isOpenApiExport)
{
    builder.Services.AddHangfire(config =>
        config.SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UsePostgreSqlStorage(options =>
                options.UseNpgsqlConnection(builder.Configuration.GetConnectionString("MainDatabase"))));
}

// Behind nginx + the Next.js BFF (which forwards X-Forwarded-For verbatim), honor the forwarded
// headers so RemoteIpAddress is the real client rather than the proxy — this is what restores
// per-IP partitioning on the auth rate limiters (login/register/refresh). ForwardLimit bounds how
// many hops are trusted (client -> nginx -> BFF) so a forged longer chain can't walk past it. The
// WebAPI :8080 is only reachable via the proxy chain on a private network, never directly by
// untrusted clients, so the upstream chain is trusted (KnownProxies/Networks cleared). If the real
// proxy-hop count differs, adjust ForwardLimit and confirm RemoteIpAddress reflects the true client.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = 2;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

var app = builder.Build();

// Must run before UseRateLimiter so the limiter partitions on the corrected client IP.
app.UseForwardedHeaders();
var enableSwagger = app.Environment.IsDevelopment()
    || ParseBool(app.Configuration["Swagger:Enable"], false);

// Configure the HTTP request pipeline.
if (enableSwagger)
{
    app.UseSwagger();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwaggerUI(options =>
    {
        options.DisplayRequestDuration();
        options.EnablePersistAuthorization();
    });
}

app.UseExceptionHandler();
app.UseHttpsRedirection();
app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapPrometheusScrapingEndpoint(); // Prometheus scrape at /metrics (internal network only)
// Liveness: process is up and can serve HTTP — no dependency checks run.
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
// Readiness: dependencies (PostgreSQL, Redis) reachable — gates traffic / deploy readiness.
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });

if (!isOpenApiExport)
{
    using var scope = app.Services.CreateScope();
    var bootstrap = scope.ServiceProvider.GetRequiredService<IAdminBootstrapService>();
    var bootstrapLogger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
        .CreateLogger("AdminBootstrap");
    var grants = await bootstrap.EnsureBootstrapAdminsAsync();
    if (grants > 0)
        bootstrapLogger.LogInformation("Admin bootstrap granted {Count} account(s).", grants);
    else
        bootstrapLogger.LogInformation("Admin bootstrap: no new grants.");
}

// NOTE: auto-migrate runs in the WORKER host only (Transcendence.Service), which is the migrations assembly
// (MigrationsAssembly("Transcendence.Service")). The WebAPI does not reference that assembly, so calling
// MigrateAsync here throws FileNotFoundException loading it — the WebAPI relies on the worker for migrations.
app.Run();

static bool ParseBool(string? raw, bool fallback)
{
    return bool.TryParse(raw, out var parsed) ? parsed : fallback;
}

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

static string BuildAuthRateLimitPartitionKey(HttpContext context)
{
    var clientIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown-ip";
    return $"ip:{clientIp}";
}

// Per-IP fixed-window partition for the public read limiters. Internal/SSR traffic (the web container's
// server-side fetches, health probes, other backend services) reaches the API directly with a
// private/loopback source address — it is our own trusted traffic and is NOT throttled (otherwise every
// SSR request would share one partition and starve under a single cap). Only public, forwarded client IPs
// (restored by UseForwardedHeaders, the same mechanism the auth limiters rely on) get a per-IP budget.
static RateLimitPartition<string> BuildIpReadPartition(HttpContext context, int permitLimit)
{
    var ip = context.Connection.RemoteIpAddress;
    if (ip is not null && ip.IsIPv4MappedToIPv6)
        ip = ip.MapToIPv4();

    if (ip is null || IsInternalAddress(ip))
        return RateLimitPartition.GetNoLimiter("internal");

    return RateLimitPartition.GetFixedWindowLimiter(
        $"ip:{ip}",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permitLimit,
            Window = TimeSpan.FromMinutes(1),
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 0
        });
}

static bool IsInternalAddress(IPAddress ip)
{
    if (IPAddress.IsLoopback(ip))
        return true;

    var bytes = ip.GetAddressBytes();
    if (ip.AddressFamily == AddressFamily.InterNetwork && bytes.Length == 4)
    {
        return bytes[0] == 10                                        // 10.0.0.0/8
            || (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) // 172.16.0.0/12
            || (bytes[0] == 192 && bytes[1] == 168)                  // 192.168.0.0/16
            || (bytes[0] == 169 && bytes[1] == 254);                 // 169.254.0.0/16 link-local
    }
    if (ip.AddressFamily == AddressFamily.InterNetworkV6)
    {
        return ip.IsIPv6LinkLocal
            || ip.IsIPv6SiteLocal
            || (bytes.Length == 16 && (bytes[0] & 0xFE) == 0xFC);    // fc00::/7 unique-local
    }
    return false;
}
