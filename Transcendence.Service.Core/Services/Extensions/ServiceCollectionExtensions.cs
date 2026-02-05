using Camille.RiotGames;
using Transcendence.Service.Core.Services.Analysis.Implementations;
using Transcendence.Service.Core.Services.Analysis.Interfaces;
using Transcendence.Service.Core.Services.Analytics.Implementations;
using Transcendence.Service.Core.Services.Analytics.Interfaces;
using Transcendence.Service.Core.Services.Auth.Implementations;
using Transcendence.Service.Core.Services.Auth.Interfaces;
using Transcendence.Service.Core.Services.Cache;
using Transcendence.Service.Core.Services.Jobs;
using Transcendence.Service.Core.Services.Jobs.Interfaces;
using Transcendence.Service.Core.Services.RiotApi.Implementations;
using Transcendence.Service.Core.Services.RiotApi.Interfaces;
using Transcendence.Service.Core.Services.StaticData.Implementations;
using Transcendence.Service.Core.Services.StaticData.Interfaces;

namespace Transcendence.Service.Core.Services.Extensions;

public static class ServiceCollectionExtensions
{
    // Core services that do not require external Riot SDK
    public static IServiceCollection AddTranscendenceCore(this IServiceCollection services)
    {
        services.AddScoped<ICacheService, CacheService>();
        services.AddScoped<IChampionLoadoutAnalysisService, ChampionLoadoutAnalysisService>();
        services.AddScoped<ISummonerStatsService, SummonerStatsService>();
        services.AddScoped<IApiKeyService, ApiKeyService>();

        // Analytics services
        services.AddScoped<IChampionAnalyticsComputeService, ChampionAnalyticsComputeService>();
        services.AddScoped<IChampionAnalyticsService, ChampionAnalyticsService>();

        // Jobs
        services.AddScoped<RefreshChampionAnalyticsJob>();

        return services;
    }

    // Riot-facing registrations; only hosts holding RiotApi:ApiKey should call this (e.g., Worker)
    public static IServiceCollection AddTranscendenceRiot(this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton(_ => RiotGamesApi.NewInstance(configuration.GetConnectionString("RiotApi")!));

        services.AddScoped<ISummonerService, SummonerService>();
        services.AddScoped<IRankService, RankService>();
        services.AddScoped<IMatchService, MatchService>();
        services.AddScoped<IStaticDataService, StaticDataService>();

        services.AddScoped<UpdateStaticDataJob>();
        services.AddScoped<ISummonerRefreshJob, SummonerRefreshJob>();
        return services;
    }
}
