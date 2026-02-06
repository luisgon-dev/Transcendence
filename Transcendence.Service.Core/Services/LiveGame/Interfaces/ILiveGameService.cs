using Transcendence.Service.Core.Services.LiveGame.Models;

namespace Transcendence.Service.Core.Services.LiveGame.Interfaces;

public interface ILiveGameService
{
    Task<LiveGameResponseDto> GetCurrentGameAsync(
        string platformRegion,
        string gameName,
        string tagLine,
        CancellationToken ct = default);
}
