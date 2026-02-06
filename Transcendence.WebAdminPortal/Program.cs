using Hangfire;
using Hangfire.PostgreSql;
using Transcendence.Service.Core.Services.Jobs;

var builder = WebApplication.CreateBuilder(args);

// Force-load job assembly so Hangfire dashboard can deserialize recurring jobs.
_ = typeof(RefreshChampionAnalyticsJob).Assembly;

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

var app = builder.Build();

app.UseHangfireDashboard();

// app.MapGet("/", () => "Hello World!");

app.Run();
