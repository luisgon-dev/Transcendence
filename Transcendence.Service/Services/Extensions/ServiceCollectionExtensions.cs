using Transcendence.Service.Services.RiotApi;
using Transcendence.Service.Services.Analysis.Implementations;
using Transcendence.Service.Services.Analysis.Interfaces;
using Transcendence.Service.Services.RiotApi.Implementations;
using Transcendence.Service.Services.RiotApi.Interfaces;

namespace Transcendence.Service.Services.Extensions;

public static class ServiceCollectionExtensions
{
    public static void AddRiotApiServiceCollection(this IServiceCollection services)
    {
        services.AddScoped<ISummonerService, SummonerService>();
        services.AddScoped<IRankService, RankService>();
        services.AddScoped<IMatchService, MatchService>();
        services.AddScoped<IChampionLoadoutAnalysisService, ChampionLoadoutAnalysisService>();
        services.AddSingleton<IMatchDataGatheringService, MatchDataGatheringService>();
        
       
    }
}