using Transcendence.Data.Models.Tft.Account;

namespace Transcendence.Data.Repositories.Interfaces;

public interface ITftRankRepository
{
    Task AddOrUpdateRankAsync(TftSummoner summoner, IReadOnlyList<TftRank> incomingRanks,
        CancellationToken cancellationToken = default);
}
