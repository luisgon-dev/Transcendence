using Hangfire;

namespace ProjectSyndraBackend.Service.Services;

public class MatchDataGatheringService(IBackgroundJobClient backgroundJobClient) : IMatchDataGatheringService
{
    public void Init()
    {
        // initialize all recurring jobs here
        
        
        // every hour check to see if a new patch is available for league.
        
    }
}