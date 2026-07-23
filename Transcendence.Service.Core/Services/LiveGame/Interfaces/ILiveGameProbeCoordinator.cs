using Camille.Enums;
using Transcendence.Service.Core.Services.LiveGame.Models;

namespace Transcendence.Service.Core.Services.LiveGame.Interfaces;

public interface ILiveGameProbeCoordinator
{
    Task<LiveGameProbeOutcome> EnqueueAsync(
        PlatformRoute platform,
        string gameName,
        string tagLine,
        CancellationToken ct = default);
}
