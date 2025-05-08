using ProjectSyndraBackend.Data.Models.Service;

namespace ProjectSyndraBackend.Service.Services.Analysis.Interfaces;

public interface IChampionLoadoutAnalysisService
{
    Task<List<CurrentChampionLoadout>> GetChampionLoadoutsAsync(CancellationToken stoppingToken);
}