using ProjectSyndraBackend.Service.Services.Analysis.Implementations;
using ProjectSyndraBackend.Service.Services.Analysis.Interfaces;
using ProjectSyndraBackend.Service.Services.RiotApi;
using ProjectSyndraBackend.Service.Services.RiotApi.Implementations;
using ProjectSyndraBackend.Service.Services.RiotApi.Interfaces;

namespace ProjectSyndraBackend.Service.Services.Extensions;

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