using Camille.RiotGames;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.EntityFrameworkCore;
using ProjectSyndraBackend.Data;
using ProjectSyndraBackend.Service;
using ProjectSyndraBackend.Service.Services.Recurring_Jobs;

var builder = Host.CreateApplicationBuilder(args);

// Add services to the container.
builder.Services.AddDbContext<ProjectSyndraContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("MainDatabase"),
        b => b.MigrationsAssembly("ProjectSyndraBackend.Service")));
builder.Services.AddSingleton(_ => RiotGamesApi.NewInstance(builder.Configuration.GetConnectionString("RiotApi")!));


GlobalConfiguration.Configuration
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UsePostgreSqlStorage(options =>
        options.UseNpgsqlConnection(builder.Configuration.GetConnectionString("MainDatabase")));

builder.Services.AddScoped<ITaskService, FetchLatestMatchInformation>();

builder.Services.AddHostedService<Worker>(provider =>
{
    var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
    var logger = provider.GetRequiredService<ILogger<Worker>>();
    return new Worker(logger, scopeFactory);
});

var host = builder.Build();
host.Run();