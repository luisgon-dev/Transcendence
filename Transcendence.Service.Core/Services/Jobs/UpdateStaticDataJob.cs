using Hangfire;
using Transcendence.Service.Core.Services.StaticData.Interfaces;

namespace Transcendence.Service.Core.Services.Jobs;

[DisableConcurrentExecution(timeoutInSeconds: 30 * 60)]
public class UpdateStaticDataJob(IStaticDataService staticDataService)
{
    public async Task Execute(CancellationToken cancellationToken)
    {
        await staticDataService.DetectAndRefreshAsync(cancellationToken);
    }
}
