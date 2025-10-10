using Transcendence.Service.Services.RiotApi;
using Transcendence.Service.Services.Analysis.Implementations;
using Transcendence.Service.Services.Analysis.Interfaces;
using Transcendence.Service.Services.RiotApi.Implementations;
using Transcendence.Service.Services.RiotApi.Interfaces;
using Transcendence.Service.Services.StaticData.Interfaces;
using Transcendence.Service.Services.StaticData.Implementations;
using Transcendence.Service.Services.Jobs;

namespace Transcendence.Service.Services.Extensions;

public static class ServiceCollectionExtensions
{
    public static void AddTranscendenceServices(this IServiceCollection services)
    {
        services.AddScoped<ISummonerService, SummonerService>();
        services.AddScoped<IRankService, RankService>();
        services.AddScoped<IMatchService, MatchService>();
        services.AddScoped<IChampionLoadoutAnalysisService, ChampionLoadoutAnalysisService>();
        services.AddScoped<ISummonerStatsService, SummonerStatsService>();
        services.AddSingleton<IMatchDataGatheringService, MatchDataGatheringService>();

        services.AddScoped<IStaticDataService, StaticDataService>();

        services.AddScoped<UpdateStaticDataJob>();
    }
}