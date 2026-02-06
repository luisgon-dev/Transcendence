using Transcendence.Service.Core.Services.LiveGame.Models;

namespace Transcendence.Service.Core.Services.LiveGame.Interfaces;

public interface ILiveGameAnalysisService
{
    Task<LiveGameAnalysisDto> AnalyzeAsync(
        string platformRegion,
        LiveGameResponseDto liveGame,
        CancellationToken ct = default);
}
