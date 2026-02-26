using Camille.RiotGames;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using System.Threading.RateLimiting;
using System.Text;
using Transcendence.Data;
using Transcendence.Data.Extensions;
using Transcendence.Service.Core.Services.Auth.Interfaces;
using Transcendence.Service.Core.Services.Auth.Models;
using Transcendence.Service.Core.Services.Analytics.Models;
using Transcendence.Service.Core.Services.Extensions;
using Transcendence.WebAPI.Security;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
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
    options.AddFixedWindowLimiter("admin-write", limiter =>
    {
        limiter.PermitLimit = 30;
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        limiter.QueueLimit = 0;
    });
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

// Register only core, API remains keyless
builder.Services.AddTranscendenceCore();
builder.Services.AddProjectSyndraRepositories();
builder.Services.Configure<ChampionAnalyticsComputeOptions>(builder.Configuration.GetSection("Analytics:Compute"));
builder.Services.Configure<AdminBootstrapOptions>(builder.Configuration.GetSection("Auth:AdminBootstrap"));

var riotApiKey = builder.Configuration.GetConnectionString("RiotApi")
                 ?? builder.Configuration["RiotApi:ApiKey"];
if (string.IsNullOrWhiteSpace(riotApiKey))
{
    throw new InvalidOperationException(
        "Missing Riot API key configuration. Set 'ConnectionStrings:RiotApi' (or 'RiotApi:ApiKey').");
}

builder.Services.AddSingleton(_ => RiotGamesApi.NewInstance(riotApiKey));

var jwtIssuer = builder.Configuration["Auth:Jwt:Issuer"] ?? "Transcendence";
var jwtAudience = builder.Configuration["Auth:Jwt:Audience"] ?? "TranscendenceClients";
var jwtKey = builder.Configuration["Auth:Jwt:Key"] ?? "CHANGE_THIS_DEV_ONLY_KEY_32_CHARS_MINIMUM";

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

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.DisplayRequestDuration();
        options.EnablePersistAuthorization();
    });
}

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
}

app.Run();
