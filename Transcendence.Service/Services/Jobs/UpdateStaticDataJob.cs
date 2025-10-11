using Transcendence.Service.Services.StaticData.Interfaces;
namespace Transcendence.Service.Services.Jobs;

public class UpdateStaticDataJob(IStaticDataService staticDataService)
{

    public async Task Execute(CancellationToken cancellationToken)
    {
        await staticDataService.UpdateStaticDataAsync(cancellationToken);
    }
}
