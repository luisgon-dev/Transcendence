using Hangfire;
using Transcendence.Service.Core.Services.Extensions;
using Transcendence.Service.Core.Services.Jobs;
using Transcendence.Service.Core.Services.StaticData.Implementations;

namespace Transcendence.Service.Workers;

public class DevelopmentWorker(IBackgroundJobClient backgroundJobClient, ILogger<DevelopmentWorker> logger)
    : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        CleanupHangfireJobs();
        BackgroundJob.Enqueue<UpdateStaticDataJob>(x => x.Execute(CancellationToken.None));
        return Task.CompletedTask;
    }

    private void CleanupHangfireJobs()
    {
        // clear any queued job or failed jobs
        JobStorage.Current?.GetMonitoringApi()?.PurgeJobs();
        RecurringJob.RemoveIfExists("*");
        logger.LogInformation("Cleared all jobs");
    }
}