using Camille.Enums;
using Transcendence.Data.Models.Tft.Account;

namespace Transcendence.Service.Core.Services.Tft.Interfaces;

public interface ITftSummonerService
{
    Task<TftSummoner> GetSummonerByPuuidAsync(string puuid, PlatformRoute platformRoute, CancellationToken ct = default);
    Task<TftSummoner> GetSummonerByRiotIdAsync(string gameName, string tagLine, PlatformRoute platformRoute,
        CancellationToken ct = default);
}
