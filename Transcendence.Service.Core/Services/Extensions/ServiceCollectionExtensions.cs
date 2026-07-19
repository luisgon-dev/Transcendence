using Transcendence.Service.Core.Services.Admin.Implementations;
using Transcendence.Service.Core.Services.Admin.Interfaces;
using Transcendence.Service.Core.Services.Analysis.Implementations;
using Transcendence.Service.Core.Services.Analysis.Interfaces;
using Transcendence.Service.Core.Services.Analytics.Implementations;
using Transcendence.Service.Core.Services.Analytics.Interfaces;
using Transcendence.Service.Core.Services.Auth.Implementations;
using Transcendence.Service.Core.Services.Auth.Interfaces;
using Transcendence.Service.Core.Services.Cache;
using Transcendence.Service.Core.Services.Diagnostics;
using Transcendence.Service.Core.Services.Jobs;
using Transcendence.Service.Core.Services.Jobs.Interfaces;
using Transcendence.Service.Core.Services.Jobs.Priority;
using Transcendence.Service.Core.Services.LiveGame.Implementations;
using Transcendence.Service.Core.Services.LiveGame.Interfaces;
using Transcendence.Service.Core.Services.RiotApi;
using Transcendence.Service.Core.Services.RiotApi.Implementations;
using Transcendence.Service.Core.Services.RiotApi.Interfaces;
using Transcendence.Service.Core.Services.StaticData.Implementations;
using Transcendence.Service.Core.Services.StaticData.Interfaces;

namespace Transcendence.Service.Core.Services.Extensions;

public static class ServiceCollectionExtensions
{
    // Shared services used by both the keyless WebAPI host and the worker host.
    public static IServiceCollection AddTranscendenceCore(this IServiceCollection services)
    {
        services.AddScoped<ICacheService, CacheService>();
        services.AddScoped<IChampionLoadoutAnalysisService, ChampionLoadoutAnalysisService>();
        services.AddScoped<ISummonerStatsService, SummonerStatsService>();
        services.AddScoped<IApiKeyService, ApiKeyService>();
        services.AddScoped<IAdminAuditService, AdminAuditService>();
        services.AddScoped<IAdminBootstrapService, AdminBootstrapService>();
        services.AddScoped<IJwtService, JwtService>();
        services.AddScoped<IUserAuthService, UserAuthService>();
        services.AddScoped<IUserPreferencesService, UserPreferencesService>();
        services.AddScoped<ILiveGameService, StoredLiveGameService>();
        services.AddScoped<ILiveGameAnalysisService, LiveGameAnalysisService>();
        services.AddScoped<IMultiSearchService, MultiSearchService>();
        services.AddSingleton<IRefreshLockLifecycleTelemetry, RefreshLockLifecycleTelemetry>();
        services.AddSingleton<IIngestionThroughputTelemetry, IngestionThroughputTelemetry>();

        // Analytics services
        services.AddScoped<IChampionWinRateComputeService, ChampionWinRateComputeService>();
        services.AddScoped<IChampionBuildComputeService, ChampionBuildComputeService>();
        services.AddScoped<IChampionProComputeService, ChampionProComputeService>();
        services.AddScoped<IChampionMatchupComputeService, ChampionMatchupComputeService>();
        services.AddScoped<IChampionAnalyticsService, ChampionAnalyticsService>();
        services.AddScoped<IPrecomputedAnalyticsRefresher, PrecomputedAnalyticsRefresher>();

        return services;
    }

    // Admin-operations facades that decompose AdminOperationsController (P10.1). Registered
    // separately from AddTranscendenceCore because they depend on the Hangfire job-storage graph
    // (JobStorage, IRecurringJobManager, IWorkerRecurringJobPolicy) which only the WebAPI host
    // configures; keeping them out of AddTranscendenceCore preserves that method's Hangfire-free
    // DI-validation contract.
    public static IServiceCollection AddAdminOperationsFacades(this IServiceCollection services)
    {
        services.AddScoped<IAdminJobsFacade, AdminJobsFacade>();
        services.AddScoped<IAdminOverviewFacade, AdminOverviewFacade>();
        services.AddScoped<IAdminLogsFacade, AdminLogsFacade>();

        return services;
    }

    // Worker-only orchestration, bootstrap, and recurring job graph.
    public static IServiceCollection AddTranscendenceWorkerCore(this IServiceCollection services)
    {
        services.AddScoped<ISummonerBootstrapService, SummonerBootstrapService>();
        services.AddScoped<ChampionAnalyticsIngestionJob>();
        services.AddScoped<RefreshChampionAnalyticsJob>();
        services.AddScoped<WarmDefaultChampionProfilesJob>();
        services.AddScoped<RefreshPrecomputedAnalyticsJob>();
        services.AddScoped<LiveGamePollingJob>();
        services.AddScoped<RuneSelectionIntegrityBackfillJob>();
        services.AddScoped<MatchTimelineBackfillJob>();
        services.AddScoped<SummonerMaintenanceJob>();
        services.AddScoped<RefreshLockLifecycleJob>();
        services.AddScoped<BackfillMatchPlatformRegionJob>();
        services.AddScoped<IngestionHealthAlertJob>();
        services.AddScoped<IIngestionPriorityScoringPolicy, IngestionPriorityScoringPolicy>();
        services.AddSingleton<IQueueDepthProbe, HangfireQueueDepthProbe>();

        return services;
    }

    // Riot-facing registrations; only hosts holding RiotApi keys should call this (e.g., Worker)
    public static IServiceCollection AddTranscendenceLeagueRiot(this IServiceCollection services,
        IConfiguration configuration)
    {
        var riotApiKey = configuration["RiotApi:League:ApiKey"];
        if (string.IsNullOrWhiteSpace(riotApiKey))
        {
            throw new InvalidOperationException(
                "Missing League Riot API key configuration. Set 'RiotApi:League:ApiKey'.");
        }

        services.AddSingleton(_ => new LeagueRiotApiContext(Camille.RiotGames.RiotGamesApi.NewInstance(riotApiKey)));
        // Per-region request-rate gate paces outbound Riot calls under the key's per-region budget so
        // Camille's limiter never saturates. Singleton (holds per-region token buckets + refill timers).
        services.AddSingleton<IRiotRateGate, RiotRateGate>();

        services.AddScoped<ISummonerService, SummonerService>();
        services.AddScoped<IRankService, RankService>();
        // Tolerant League-V4 entries fallback (enum-free) for when Riot returns a queueType Camille's
        // latest nightly doesn't model. Binds the Riot key on the typed client; used only on that failure.
        services.AddHttpClient<IRankFallbackClient, RankFallbackClient>(client =>
        {
            client.DefaultRequestHeaders.Add("X-Riot-Token", riotApiKey);
            client.Timeout = TimeSpan.FromSeconds(15);
        });
        services.AddScoped<IChampionMasteryService, ChampionMasteryService>();
        services.AddScoped<IMatchService, MatchService>();
        services.AddScoped<IStaticDataService, StaticDataService>();
        services.AddScoped<IRiotMatchIdsClient, RiotMatchIdsClient>();

        services.AddScoped<UpdateStaticDataJob>();
        services.AddScoped<RetryFailedMatchesJob>();
        services.AddScoped<ISummonerRefreshJob, SummonerRefreshJob>();
        services.AddScoped<FullHistoryBackfillJob>();
        services.AddScoped<MatchTimelineIngestionJob>();
        services.AddScoped<IRiotAccountService, RiotAccountService>();
        services.AddScoped<ILiveGamePollingService, RiotLiveGamePollingService>();
        return services;
    }
}
