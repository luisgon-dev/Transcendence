using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.EntityFrameworkCore;
using Transcendence.Data;
using Transcendence.Data.Extensions;
using Transcendence.Service.Core.Services.Extensions;
using Transcendence.Service.Workers;

var builder = Host.CreateApplicationBuilder(args);

// Add services to the container.
builder.Services.AddDbContext<TranscendenceContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("MainDatabase"),
        b => b.MigrationsAssembly("Transcendence.Service")));

builder.Services.AddHangfire(config =>
    config.SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
        .UseSimpleAssemblyNameTypeSerializer()
        .UseRecommendedSerializerSettings()
        .UseFilter(new AutomaticRetryAttribute
        {
            Attempts = 0
        })
        .UsePostgreSqlStorage(options =>
            options.UseNpgsqlConnection(builder.Configuration.GetConnectionString("MainDatabase"))));
builder.Services.AddHangfireServer();

builder.Services.AddHttpClient();

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