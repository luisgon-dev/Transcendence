using Transcendence.Data.Models.Service;
using Transcendence.Service.Core.Analysis.Interfaces;
namespace Transcendence.Service.Core.Analysis.Implementations;

public class ChampionLoadoutAnalysisService : IChampionLoadoutAnalysisService
{
    public Task<List<CurrentChampionLoadout>> GetChampionLoadoutsAsync(CancellationToken stoppingToken)
    {
        throw new NotImplementedException();
    }
}
