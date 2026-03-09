using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using System.Threading.RateLimiting;
using System.Text;
using Transcendence.Data;
using Transcendence.Data.Extensions;
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

builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddFixedWindowLimiter("expensive-read", limiter =>
    {
        limiter.PermitLimit = 120;
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        limiter.QueueLimit = 0;
    });
    options.AddFixedWindowLimiter("search-read", limiter =>
    {
        limiter.PermitLimit = 600;
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        limiter.QueueLimit = 0;
    });
    options.AddFixedWindowLimiter("multisearch-read", limiter =>
    {
        limiter.PermitLimit = 60;
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        limiter.QueueLimit = 0;
    });
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
});

// Infrastructure: DbContext, HTTP, domain services, repositories
builder.Services.AddDbContextPool<TranscendenceContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("MainDatabase"),
        b => b.MigrationsAssembly("Transcendence.Service")));
builder.Services.AddHealthChecks();

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

// Register core services plus Riot-backed dependencies required by the shared job graph.
builder.Services.AddTranscendenceCore();
builder.Services.AddTranscendenceLeagueRiot(builder.Configuration);
builder.Services.AddTranscendenceTftRiot(builder.Configuration);
builder.Services.AddProjectSyndraRepositories();
builder.Services.Configure<ChampionAnalyticsComputeOptions>(builder.Configuration.GetSection("Analytics:Compute"));
builder.Services.Configure<ChampionAnalyticsIngestionJobOptions>(
    builder.Configuration.GetSection("Jobs:ChampionAnalyticsIngestion"));
builder.Services.Configure<MultiRegionIngestionOptions>(builder.Configuration.GetSection("Jobs:MultiRegionIngestion"));
builder.Services.Configure<WorkerJobScheduleOptions>(builder.Configuration.GetSection("Jobs:Schedule"));
builder.Services.Configure<WorkerSchedulingProfileOptions>(builder.Configuration.GetSection("Jobs:SchedulingProfiles"));
builder.Services.Configure<AdminBootstrapOptions>(builder.Configuration.GetSection("Auth:AdminBootstrap"));
builder.Services.AddSingleton<IWorkerRecurringJobPolicy, WorkerRecurringJobPolicy>();
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

// Configure Hangfire client (no server) for enqueueing jobs
builder.Services.AddHangfire(config =>
    config.SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
        .UseSimpleAssemblyNameTypeSerializer()
        .UseRecommendedSerializerSettings()
        .UsePostgreSqlStorage(options =>
            options.UseNpgsqlConnection(builder.Configuration.GetConnectionString("MainDatabase"))));

var app = builder.Build();
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
app.MapHealthChecks("/health/live");
app.MapHealthChecks("/health/ready");

using (var scope = app.Services.CreateScope())
{
    var bootstrap = scope.ServiceProvider.GetRequiredService<IAdminBootstrapService>();
    var bootstrapLogger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
        .CreateLogger("AdminBootstrap");
    var grants = await bootstrap.EnsureBootstrapAdminsAsync();
    if (grants > 0)
        bootstrapLogger.LogInformation("Admin bootstrap granted {Count} account(s).", grants);
    else
        bootstrapLogger.LogInformation("Admin bootstrap: no new grants.");
}

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
