using Camille.RiotGames;
using Transcendence.Service.Core.Analysis.Implementations;
using Transcendence.Service.Core.Analysis.Interfaces;
using Transcendence.Service.Core.Jobs;
using Transcendence.Service.Core.Jobs.Interfaces;
using Transcendence.Service.Core.RiotApi.Implementations;
using Transcendence.Service.Core.RiotApi.Interfaces;
using Transcendence.Service.Core.StaticData.Implementations;
using Transcendence.Service.Core.StaticData.Interfaces;
namespace Transcendence.Service.Core.Extensions;

public static class ServiceCollectionExtensions
{
    // Core services that do not require external Riot SDK
    public static IServiceCollection AddTranscendenceCore(this IServiceCollection services)
    {
        services.AddScoped<IChampionLoadoutAnalysisService, ChampionLoadoutAnalysisService>();
        services.AddScoped<ISummonerStatsService, SummonerStatsService>();
        return services;
    }

    // Riot-facing registrations; only hosts holding RiotApi:ApiKey should call this (e.g., Worker)
    public static IServiceCollection AddTranscendenceRiot(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton(_ => RiotGamesApi.NewInstance(configuration["RiotApi:ApiKey"]!));

        services.AddScoped<ISummonerService, SummonerService>();
        services.AddScoped<IRankService, RankService>();
        services.AddScoped<IMatchService, MatchService>();
        services.AddScoped<IStaticDataService, StaticDataService>();

        services.AddScoped<UpdateStaticDataJob>();
        services.AddScoped<ISummonerRefreshJob, SummonerRefreshJob>();
        return services;
    }
}
