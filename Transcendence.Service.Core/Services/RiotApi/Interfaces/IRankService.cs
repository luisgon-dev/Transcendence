using Camille.Enums;
using Transcendence.Data.Models.LoL.Account;
namespace Transcendence.Service.Core.RiotApi.Interfaces;

public interface IRankService
{
    Task<List<Rank>> GetRankedDataAsync(string summonerPuuid, PlatformRoute platformRoute,
        CancellationToken cancellationToken = default);
}
