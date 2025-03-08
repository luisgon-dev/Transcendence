using ProjectSyndraBackend.Service.Services.RiotApi;
using ProjectSyndraBackend.Service.Services.RiotApi.Implementations;
using ProjectSyndraBackend.Service.Services.RiotApi.Interfaces;

namespace ProjectSyndraBackend.Service.Services.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddRiotApiServiceCollection(this IServiceCollection services)
    {
        services.AddScoped<ISummonerService, SummonerService>();
        services.AddScoped<IRankService, RankService>();
        services.AddScoped<IMatchService, MatchService>();

        services.AddSingleton<IMatchDataGatheringService, MatchDataGatheringService>();


        return services;
    }
}