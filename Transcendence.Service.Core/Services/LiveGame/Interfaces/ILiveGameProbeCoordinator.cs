using Camille.Enums;
using Transcendence.Service.Core.Services.LiveGame.Models;
using Transcendence.Service.Core.Services.Operations;

namespace Transcendence.Service.Core.Services.LiveGame.Interfaces;

public interface ILiveGameProbeCoordinator
{
    Task<LiveGameProbeOutcome> EnqueueAsync(
        PlatformRoute platform,
        string gameName,
        string tagLine,
        OperationOwner owner,
        CancellationToken ct = default);
}
