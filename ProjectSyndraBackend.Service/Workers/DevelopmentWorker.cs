using Hangfire;
using ProjectSyndraBackend.Service.Services.Extensions;
using ProjectSyndraBackend.Service.Services.Jobs;

namespace ProjectSyndraBackend.Service.Workers;

public class DevelopmentWorker : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        CleanupHangfireJobs();
        
        
        BackgroundJob.Enqueue<AddOrUpdateHighEloProfiles>(x => x.Execute(CancellationToken.None));
        return Task.CompletedTask;
    }
    
    // function to cleanup all hangfire jobs

    private void CleanupHangfireJobs()
    {
        // clear any queued job or failed jobs
        JobStorage.Current?.GetMonitoringApi()?.PurgeJobs();  
    }
}