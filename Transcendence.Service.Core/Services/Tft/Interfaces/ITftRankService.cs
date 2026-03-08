using Camille.Enums;
using Transcendence.Data.Models.Tft.Account;

namespace Transcendence.Service.Core.Services.Tft.Interfaces;

public interface ITftRankService
{
    Task<List<TftRank>> GetRankedDataAsync(string summonerPuuid, PlatformRoute platformRoute, CancellationToken ct = default);
}
