using Transcendence.Service.Core.Services.LiveGame.Models;

namespace Transcendence.Service.Core.Services.LiveGame.Interfaces;

public interface ILiveGamePollingService
{
    Task<LiveGameResponseDto> FetchCurrentGameAsync(
        string platformRegion,
        string gameName,
        string tagLine,
        CancellationToken ct = default);

    Task<LiveGameResponseDto> ProbeCurrentGameAsync(
        string platformRegion,
        string gameName,
        string tagLine,
        CancellationToken ct = default);
}
