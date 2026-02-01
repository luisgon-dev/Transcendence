namespace Transcendence.Service.Core.Services.StaticData.Interfaces;

public interface IStaticDataService
{
    Task UpdateStaticDataAsync(CancellationToken cancellationToken = default);
    Task EnsureStaticDataForPatchAsync(string patchVersion, CancellationToken cancellationToken = default);
}