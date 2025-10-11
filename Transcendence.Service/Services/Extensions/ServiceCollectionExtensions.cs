using Camille.RiotGames;
using Microsoft.Extensions.Configuration;
using Transcendence.Service.Services.Analysis.Implementations;
using Transcendence.Service.Services.Analysis.Interfaces;
using Transcendence.Service.Services.Jobs;
using Transcendence.Service.Services.RiotApi.Implementations;
using Transcendence.Service.Services.RiotApi.Interfaces;
using Transcendence.Service.Services.StaticData.Implementations;
using Transcendence.Service.Services.StaticData.Interfaces;

namespace Transcendence.Service.Services.Extensions;

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

        services.AddSingleton<IMatchDataGatheringService, MatchDataGatheringService>();
        services.AddScoped<UpdateStaticDataJob>();
        services.AddScoped<Transcendence.Service.Services.Jobs.Interfaces.ISummonerRefreshJob, Transcendence.Service.Services.Jobs.SummonerRefreshJob>();
        return services;
    }
}