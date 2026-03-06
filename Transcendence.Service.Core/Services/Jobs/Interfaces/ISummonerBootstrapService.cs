namespace Transcendence.Service.Core.Services.Jobs.Interfaces;

public interface ISummonerBootstrapService
{
    Task<int> EnsureSeededFromChallengerAsync(CancellationToken ct = default);
    Task<int> EnsureSeededForRegionAsync(string platformRegion, CancellationToken ct = default);
}
