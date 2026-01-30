using Transcendence.Service.Core.StaticData.Interfaces;
namespace Transcendence.Service.Core.Jobs;

public class UpdateStaticDataJob(IStaticDataService staticDataService)
{

    public async Task Execute(CancellationToken cancellationToken)
    {
        await staticDataService.UpdateStaticDataAsync(cancellationToken);
    }
}
