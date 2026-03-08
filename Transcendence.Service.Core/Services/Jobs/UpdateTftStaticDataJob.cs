using Hangfire;
using Transcendence.Service.Core.Services.Tft.Interfaces;

namespace Transcendence.Service.Core.Services.Jobs;

[DisableConcurrentExecution(timeoutInSeconds: 30 * 60)]
public class UpdateTftStaticDataJob(ITftStaticDataService staticDataService)
{
    [Queue("tft-default")]
    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        await staticDataService.UpdateStaticDataAsync(cancellationToken);
    }
}
