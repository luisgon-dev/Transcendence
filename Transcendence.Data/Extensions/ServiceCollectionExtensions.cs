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
        services.AddScoped<IApiClientKeyRepository, ApiClientKeyRepository>();
        services.AddScoped<ILiveGameSnapshotRepository, LiveGameSnapshotRepository>();
        services.AddScoped<IUserAccountRepository, UserAccountRepository>();
        services.AddScoped<IUserPreferencesRepository, UserPreferencesRepository>();

        return services;
    }
}
