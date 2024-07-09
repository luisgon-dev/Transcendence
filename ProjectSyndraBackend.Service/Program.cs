using Camille.RiotGames;
using Microsoft.EntityFrameworkCore;
using ProjectSyndraBackend.Service;
using ProjectSyndraBackend.Data;
using ProjectSyndraBackend.Service.Services;

var builder = Host.CreateApplicationBuilder(args);

// Add services to the container.
builder.Services.AddDbContext<ProjectSyndraContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("MainDatabase"), b => b.MigrationsAssembly("ProjectSyndraBackend.Service")));
builder.Services.AddSingleton(_ => RiotGamesApi.NewInstance(builder.Configuration.GetConnectionString("RiotApi")!));
builder.Services.AddScoped<ITaskService, FetchPatchStats>();



builder.Services.AddHostedService<Worker>(provider =>
{
    var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
    var logger = provider.GetRequiredService<ILogger<Worker>>();
    return new Worker(logger, scopeFactory);
});

var host = builder.Build();
host.Run();