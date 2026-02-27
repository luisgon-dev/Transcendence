using Camille.Enums;

namespace Transcendence.Service.Core.Services.RiotApi.Interfaces;

public interface IRiotAccountService
{
    Task<string?> ResolvePuuidAsync(string gameName, string tagLine, PlatformRoute platform,
        CancellationToken ct = default);
}
