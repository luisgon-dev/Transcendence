using Hangfire;
using ProjectSyndraBackend.Service.Services.Jobs;

namespace ProjectSyndraBackend.Service.Services;

public class MatchDataGatheringService(IBackgroundJobClient backgroundJobClient) : IMatchDataGatheringService
{
    public void Init()
    {
        // initialize all recurring jobs here
        // every hour check to see if a new patch is available for league.
        RecurringJob.AddOrUpdate<UpdateParameters>("addorupdate",x => x.Execute(CancellationToken.None), Cron.Hourly);
    }
}