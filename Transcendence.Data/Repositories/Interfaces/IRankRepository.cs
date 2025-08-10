using Transcendence.Data.Models.LoL.Account;

namespace Transcendence.Data.Repositories.Interfaces;

public interface IRankRepository
{
    Task AddOrUpdateRank(List<Rank> ranks, CancellationToken cancellationToken = default);
}