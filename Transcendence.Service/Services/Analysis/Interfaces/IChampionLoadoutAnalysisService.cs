using Transcendence.Data.Models.Service;

namespace Transcendence.Service.Services.Analysis.Interfaces;

public interface IChampionLoadoutAnalysisService
{
    Task<List<CurrentChampionLoadout>> GetChampionLoadoutsAsync(CancellationToken stoppingToken);
}