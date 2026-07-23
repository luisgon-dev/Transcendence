using Hangfire;
using Transcendence.Service.Core.Services.StaticData.Interfaces;

namespace Transcendence.Service.Core.Services.Jobs;

[DisableConcurrentExecution(timeoutInSeconds: 30 * 60)]
public class UpdateStaticDataJob(
    IStaticDataService staticDataService,
    IBackgroundJobClient backgroundJobClient)
{
    public async Task Execute(CancellationToken cancellationToken)
    {
        await staticDataService.DetectAndRefreshAsync(cancellationToken);
        backgroundJobClient.Enqueue<RefreshBuildResourceAnalyticsJob>(
            job => job.ExecuteAsync(true, false, CancellationToken.None));
    }
}
