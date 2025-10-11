using Transcendence.Data.Models.Service;
using Transcendence.Service.Services.Analysis.Interfaces;
namespace Transcendence.Service.Services.Analysis.Implementations;

public class ChampionLoadoutAnalysisService : IChampionLoadoutAnalysisService
{
    public Task<List<CurrentChampionLoadout>> GetChampionLoadoutsAsync(CancellationToken stoppingToken)
    {
        throw new NotImplementedException();
    }
}
