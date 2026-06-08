using Transcendence.Data.Models.LoL.Account;

namespace Transcendence.Data.Repositories.Interfaces;

public interface IChampionMasteryRepository
{
    /// <summary>
    /// Replaces a summoner's champion-mastery snapshot with the supplied list
    /// (insert/update by champion, prune champions no longer present). Caller saves.
    /// </summary>
    Task UpsertAsync(Guid summonerId, List<ChampionMastery> masteries, CancellationToken cancellationToken = default);
}
