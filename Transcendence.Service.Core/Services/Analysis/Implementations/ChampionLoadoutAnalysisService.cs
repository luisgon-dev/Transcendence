using Transcendence.Data.Models.Service;
using Transcendence.Service.Core.Services.Analysis.Interfaces;

namespace Transcendence.Service.Core.Services.Analysis.Implementations;

public class ChampionLoadoutAnalysisService : IChampionLoadoutAnalysisService
{
    public Task<List<CurrentChampionLoadout>> GetChampionLoadoutsAsync(CancellationToken stoppingToken)
    {
        throw new NotImplementedException();
    }
}