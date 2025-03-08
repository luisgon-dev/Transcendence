using Camille.RiotGames;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.EntityFrameworkCore;
using ProjectSyndraBackend.Data;
using ProjectSyndraBackend.Data.Extensions;
using ProjectSyndraBackend.Service;
using ProjectSyndraBackend.Service.Services.Extensions;

var builder = Host.CreateApplicationBuilder(args);

// Add services to the container.
builder.Services.AddDbContext<ProjectSyndraContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("MainDatabase"),
        b => b.MigrationsAssembly("ProjectSyndraBackend.Service")));
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
builder.Services.AddHostedService<Worker>();

// add services
builder.Services.AddRiotApiServiceCollection();
// add data repositories
builder.Services.AddProjectSyndraRepositories();

var host = builder.Build();
host.Run();