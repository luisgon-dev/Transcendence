using Hangfire;
using Transcendence.Service.Services.Jobs;
using Transcendence.Service.Services.RiotApi.Interfaces;

namespace Transcendence.Service.Services.RiotApi.Implementations;

public class MatchDataGatheringService : IMatchDataGatheringService
{
    public void Init()
    {
        // initialize all recurring jobs here
        // every hour check to see if a new patch is available for league.
        RecurringJob.AddOrUpdate<UpdateParameters>("addorupdate", x => x.Execute(CancellationToken.None), Cron.Hourly);
        RecurringJob.AddOrUpdate<AddOrUpdateHighEloProfiles>("fetchHighEloPlayers",
            x => x.Execute(CancellationToken.None), Cron.Daily);
        RecurringJob.AddOrUpdate<FetchLatestMatchInformation>("fetchLatestMatchInformation",
            x => x.Execute(CancellationToken.None), Cron.Hourly);
    }
}