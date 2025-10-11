using Hangfire;
using Transcendence.Service.Services.Extensions;
namespace Transcendence.Service.Workers;

public class DevelopmentWorker(IBackgroundJobClient backgroundJobClient, ILogger<DevelopmentWorker> logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        CleanupHangfireJobs();
        return Task.CompletedTask;
    }

    void CleanupHangfireJobs()
    {
        // clear any queued job or failed jobs
        JobStorage.Current?.GetMonitoringApi()?.PurgeJobs();
        RecurringJob.RemoveIfExists("*");
        logger.LogInformation("Cleared all jobs");
    }
}
