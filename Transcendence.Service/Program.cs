using Camille.RiotGames;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.EntityFrameworkCore;
using Transcendence.Data;
using Transcendence.Data.Extensions;
using Transcendence.Service;
using Transcendence.Service.Services.Extensions;
using Transcendence.Service.Workers;

var builder = Host.CreateApplicationBuilder(args);

// Add services to the container.
builder.Services.AddDbContext<TranscendenceContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("MainDatabase"),
        b => b.MigrationsAssembly("Transcendence.Service")));
// inject top level riot games api
builder.Services.AddSingleton(_ => RiotGamesApi.NewInstance(builder.Configuration.GetConnectionString("RiotApi")!));
builder.Services.AddHangfire(config =>
    config.SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
        .UseSimpleAssemblyNameTypeSerializer()
        .UseRecommendedSerializerSettings()
        .UseFilter(new AutomaticRetryAttribute { Attempts = 0 })
        .UsePostgreSqlStorage(options =>
            options.UseNpgsqlConnection(builder.Configuration.GetConnectionString("MainDatabase"))));
builder.Services.AddHangfireServer();

// worker that initiates services
if (builder.Environment.IsDevelopment())
{
    // development worker directly enqueues and cleans up jobs for development
    builder.Services.AddHostedService<DevelopmentWorker>();
}
else
{
    builder.Services.AddHostedService<ProductionWorker>();
}

// check to see if we are in a dev env
// add the development service 
builder.Services.AddRiotApiServiceCollection();

// add data repositories
builder.Services.AddProjectSyndraRepositories();

var host = builder.Build();
host.Run();