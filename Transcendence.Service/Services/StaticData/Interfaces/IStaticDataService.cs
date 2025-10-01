namespace Transcendence.Service.Services.StaticData.Interfaces;

public interface IStaticDataService
{
    Task UpdateStaticDataAsync(CancellationToken cancellationToken = default);
}
