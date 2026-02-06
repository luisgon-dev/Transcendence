using Microsoft.EntityFrameworkCore;
using Transcendence.Data.Models.LoL.Account;
using Transcendence.Data.Repositories.Interfaces;

namespace Transcendence.Data.Repositories.Implementations;

public class RankRepository(TranscendenceContext context) : IRankRepository
{
    public async Task AddOrUpdateRank(Summoner summoner, List<Rank> newRanks,
        CancellationToken cancellationToken = default)
    {
        if (newRanks == null || newRanks.Count == 0) return; // No ranks to add or update

        // Load existing ranks for this summoner once
        var existingRanks = await context.Ranks
            .Where(r => r.SummonerId == summoner.Id)
            .Include(r => r.Summoner)
            .ToListAsync(cancellationToken);

        foreach (var incoming in newRanks)
        {
            var existing = existingRanks.FirstOrDefault(r => r.QueueType == incoming.QueueType);

            if (existing != null)
            {
                // Determine if any relevant fields changed
                var changed = existing.Tier != incoming.Tier ||
                              existing.RankNumber != incoming.RankNumber ||
                              existing.LeaguePoints != incoming.LeaguePoints ||
                              existing.Wins != incoming.Wins ||
                              existing.Losses != incoming.Losses;

                if (changed)
                {
                    // Snapshot previous state into history (only if a different state isn't already recorded at latest)
                    var hasLatestSnapshot = await context.HistoricalRanks
                        .AnyAsync(hr =>
                                hr.Summoner != null &&
                                hr.Summoner.Id == summoner.Id &&
                                hr.QueueType == existing.QueueType &&
                                hr.Tier == existing.Tier &&
                                hr.RankNumber == existing.RankNumber &&
                                hr.LeaguePoints == existing.LeaguePoints &&
                                hr.Wins == existing.Wins &&
                                hr.Losses == existing.Losses,
                            cancellationToken);

                    if (!hasLatestSnapshot)
                        await context.HistoricalRanks.AddAsync(new HistoricalRank
                        {
                            QueueType = existing.QueueType,
                            Tier = existing.Tier,
                            RankNumber = existing.RankNumber,
                            LeaguePoints = existing.LeaguePoints,
                            Wins = existing.Wins,
                            Losses = existing.Losses,
                            Summoner = existing.Summoner,
                            DateRecorded = DateTime.UtcNow
                        }, cancellationToken);

                    // Update current values
                    existing.Tier = incoming.Tier;
                    existing.RankNumber = incoming.RankNumber;
                    existing.LeaguePoints = incoming.LeaguePoints;
                    existing.Wins = incoming.Wins;
                    existing.Losses = incoming.Losses;
                }
            }
            else
            {
                // Attach to summoner and add as new current rank
                incoming.Summoner = summoner;
                await context.Ranks.AddAsync(incoming, cancellationToken);
            }
        }
    }
}
