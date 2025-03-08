using Microsoft.Extensions.DependencyInjection;
using ProjectSyndraBackend.Data.Repositories;
using ProjectSyndraBackend.Data.Repositories.Implementations;
using ProjectSyndraBackend.Data.Repositories.Interfaces;

namespace ProjectSyndraBackend.Data.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddProjectSyndraRepositories(this IServiceCollection services)
    {
        services.AddScoped<IMatchRepository, MatchRepository>();
        services.AddScoped<ISummonerRepository, SummonerRepository>();

        return services;
    }
}