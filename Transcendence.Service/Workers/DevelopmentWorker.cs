using Hangfire;
using Transcendence.Service.Services.Extensions;
using Transcendence.Service.Services.Jobs;

namespace Transcendence.Service.Workers;

public class DevelopmentWorker : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        CleanupHangfireJobs();
        //BackgroundJob.Enqueue<AddOrUpdateHighEloProfiles>(x => x.Execute(CancellationToken.None));
        BackgroundJob.Enqueue<FetchLatestMatchInformation>(x => x.Execute(CancellationToken.None));
        return Task.CompletedTask;
    }
    
    // function to cleanup all hangfire jobs

    private void CleanupHangfireJobs()
    {
        // clear any queued job or failed jobs
        JobStorage.Current?.GetMonitoringApi()?.PurgeJobs();  
    }
}