using ProjectSyndraBackend.Data.Models.LoL.Account;

namespace ProjectSyndraBackend.Data.Repositories.Interfaces;

public interface IRankRepository
{
    Task AddOrUpdateRank(List<Rank> ranks, CancellationToken cancellationToken = default);
}