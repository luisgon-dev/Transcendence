namespace Transcendence.Service.Core.Services.Jobs.Interfaces;

public interface ISummonerBootstrapService
{
    Task<int> EnsureSeededFromChallengerAsync(CancellationToken ct = default);
}

