using Transcendence.Service.Core.Services.StaticData.Interfaces;

namespace Transcendence.Service.Core.Services.Jobs;

public class UpdateStaticDataJob(IStaticDataService staticDataService)
{
    public async Task Execute(CancellationToken cancellationToken)
    {
        await staticDataService.UpdateStaticDataAsync(cancellationToken);
    }
}