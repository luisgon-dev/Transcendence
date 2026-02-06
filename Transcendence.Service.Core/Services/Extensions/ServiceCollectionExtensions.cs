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
using Transcendence.Service.Core.Services.LiveGame.Implementations;
using Transcendence.Service.Core.Services.LiveGame.Interfaces;
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
        services.AddScoped<IJwtService, JwtService>();
        services.AddScoped<IUserAuthService, UserAuthService>();
        services.AddScoped<IUserPreferencesService, UserPreferencesService>();
        services.AddScoped<ILiveGameService, LiveGameService>();
        services.AddScoped<ILiveGameAnalysisService, LiveGameAnalysisService>();

        // Analytics services
        services.AddScoped<IChampionAnalyticsComputeService, ChampionAnalyticsComputeService>();
        services.AddScoped<IChampionAnalyticsService, ChampionAnalyticsService>();

        // Jobs
        services.AddScoped<ChampionAnalyticsIngestionJob>();
        services.AddScoped<RefreshChampionAnalyticsJob>();
        services.AddScoped<LiveGamePollingJob>();

        return services;
    }

    // Riot-facing registrations; only hosts holding RiotApi:ApiKey should call this (e.g., Worker)
    public static IServiceCollection AddTranscendenceRiot(this IServiceCollection services,
        IConfiguration configuration)
    {
        var riotApiKey = configuration.GetConnectionString("RiotApi")
                         ?? configuration["RiotApi:ApiKey"];
        if (string.IsNullOrWhiteSpace(riotApiKey))
        {
            throw new InvalidOperationException(
                "Missing Riot API key configuration. Set 'ConnectionStrings:RiotApi' (or 'RiotApi:ApiKey').");
        }

        services.AddSingleton(_ => RiotGamesApi.NewInstance(riotApiKey));

        services.AddScoped<ISummonerService, SummonerService>();
        services.AddScoped<IRankService, RankService>();
        services.AddScoped<IMatchService, MatchService>();
        services.AddScoped<IStaticDataService, StaticDataService>();

        services.AddScoped<UpdateStaticDataJob>();
        services.AddScoped<RetryFailedMatchesJob>();
        services.AddScoped<ISummonerRefreshJob, SummonerRefreshJob>();
        return services;
    }
}
