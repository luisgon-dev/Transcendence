using Hangfire;
using ProjectSyndraBackend.Service.Services.Jobs;
using ProjectSyndraBackend.Service.Services.RiotApi.Interfaces;

namespace ProjectSyndraBackend.Service.Services.RiotApi.Implementations;

public class MatchDataGatheringService(IBackgroundJobClient backgroundJobClient) : IMatchDataGatheringService
{
    public void Init()
    {
        // initialize all recurring jobs here
        // every hour check to see if a new patch is available for league.
        RecurringJob.AddOrUpdate<UpdateParameters>("addorupdate", x => x.Execute(CancellationToken.None), Cron.Hourly);
        RecurringJob.AddOrUpdate<AddOrUpdateHighEloProfiles>("fetchHighEloPlayers",
            x => x.Execute(CancellationToken.None), Cron.Hourly);
        RecurringJob.AddOrUpdate<FetchLatestMatchInformation>("fetchLatestMatchInformation",
            x => x.Execute(CancellationToken.None), Cron.Hourly);
    }
}