using Transcendence.Service.Services.StaticData.Interfaces;

namespace Transcendence.Service.Services.Jobs;

public class UpdateStaticDataJob
{
    private readonly IStaticDataService _staticDataService;

    public UpdateStaticDataJob(IStaticDataService staticDataService)
    {
        _staticDataService = staticDataService;
    }

    public async Task Execute(CancellationToken cancellationToken)
    {
        await _staticDataService.UpdateStaticDataAsync(cancellationToken);
    }
}
