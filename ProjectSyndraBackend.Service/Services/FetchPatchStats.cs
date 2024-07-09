using Camille.RiotGames;
using ProjectSyndraBackend.Data;

namespace ProjectSyndraBackend.Service.Services;

public class FetchPatchStats(RiotGamesApi riotGamesApi, ProjectSyndraContext context, ILogger<FetchPatchStats> logger) : ITaskService
{
    public async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Fetching patch stats");
        await Task.Delay(5000, stoppingToken);
    }

    public int Interval { get; } = 5000;
}