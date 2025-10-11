using Microsoft.Extensions.DependencyInjection;
using Transcendence.Data.Repositories.Implementations;
using Transcendence.Data.Repositories.Interfaces;
namespace Transcendence.Data.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddProjectSyndraRepositories(this IServiceCollection services)
    {
        services.AddScoped<IMatchRepository, MatchRepository>();
        services.AddScoped<ISummonerRepository, SummonerRepository>();
        services.AddScoped<IRankRepository, RankRepository>();
        services.AddScoped<IRefreshLockRepository, RefreshLockRepository>();

        return services;
    }
}
