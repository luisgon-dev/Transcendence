namespace Transcendence.Service.Core.Services.Tft.Interfaces;

public interface ITftSummonerBootstrapService
{
    Task EnsureSeededFromTopLadderAsync(CancellationToken ct = default);
}
