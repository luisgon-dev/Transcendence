using ProjectSyndraBackend.Data.Models.Service;
using ProjectSyndraBackend.Service.Services.Analysis.Interfaces;

namespace ProjectSyndraBackend.Service.Services.Analysis.Implementations;

public class ChampionLoadoutAnalysisService : IChampionLoadoutAnalysisService
{
    public Task<List<CurrentChampionLoadout>> GetChampionLoadoutsAsync(CancellationToken stoppingToken)
    {
        throw new NotImplementedException();
    }
}