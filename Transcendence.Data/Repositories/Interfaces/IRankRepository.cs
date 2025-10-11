using Transcendence.Data.Models.LoL.Account;
namespace Transcendence.Data.Repositories.Interfaces;

public interface IRankRepository
{
    Task AddOrUpdateRank(Summoner summoner, List<Rank> newRanks, CancellationToken cancellationToken = default);
}
