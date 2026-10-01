using Camille.Enums;
using Transcendence.Service.Core.Services.RiotApi.Implementations;

namespace Transcendence.Service.Core.Services.RiotApi.Interfaces;

public interface IRiotSpectatorClient
{
    /// <summary>Null means a verified Spectator HTTP 404 only.</summary>
    Task<RiotSpectatorGame?> GetCurrentGameAsync(PlatformRoute platform, string puuid, CancellationToken ct = default);
}
