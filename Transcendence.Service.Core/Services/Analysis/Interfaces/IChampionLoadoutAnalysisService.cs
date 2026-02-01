using Transcendence.Data.Models.Service;

namespace Transcendence.Service.Core.Services.Analysis.Interfaces;

public interface IChampionLoadoutAnalysisService
{
    Task<List<CurrentChampionLoadout>> GetChampionLoadoutsAsync(CancellationToken stoppingToken);
}