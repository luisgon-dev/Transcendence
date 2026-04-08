namespace Transcendence.Service.Core.Services.StaticData.Interfaces;

public interface IStaticDataService
{
    Task<string?> GetLatestPatchVersionAsync(CancellationToken cancellationToken = default);
    Task UpdateStaticDataAsync(CancellationToken cancellationToken = default);
    Task EnsureStaticDataForPatchAsync(string patchVersion, CancellationToken cancellationToken = default);
    Task DetectAndRefreshAsync(CancellationToken cancellationToken = default);
}
