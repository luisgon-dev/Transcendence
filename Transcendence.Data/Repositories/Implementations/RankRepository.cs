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
                    // Snapshot the previous state into history unless it is already the latest
                    // snapshot. Only the latest counts: an identical state from an earlier season
                    // is a new point in the history, not a duplicate.
                    var latest = await context.HistoricalRanks.AsNoTracking()
                        .Where(hr =>
                            EF.Property<Guid?>(hr, "SummonerId") == summoner.Id &&
                            hr.QueueType == existing.QueueType)
                        .OrderByDescending(hr => hr.DateRecorded)
                        .FirstOrDefaultAsync(cancellationToken);
                    var hasLatestSnapshot = latest != null &&
                                            latest.Tier == existing.Tier &&
                                            latest.RankNumber == existing.RankNumber &&
                                            latest.LeaguePoints == existing.LeaguePoints &&
                                            latest.Wins == existing.Wins &&
                                            latest.Losses == existing.Losses;

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
