using Microsoft.EntityFrameworkCore;
using Transcendence.Data;
using Transcendence.Data.Models.LoL.Account;
using Transcendence.Data.Repositories.Interfaces;

public class RankRepository(TranscendenceContext context) : IRankRepository
{
    public async Task AddOrUpdateRank(List<Rank> ranks, CancellationToken cancellationToken = default)
    {
        // Check if the ranks list is empty
        if (ranks == null || ranks.Count == 0)
        {
            return; // No ranks to add or update
        }

        // Get the existing ranks from the database
        var existingRanks = await context.Ranks
            .Where(r => ranks.Select(rank => rank.SummonerId).Contains(r.SummonerId))
            .Include(rank => rank.Summoner)
            .ToListAsync(cancellationToken);

        foreach (var rank in ranks)
        {
            var existingRank = existingRanks.FirstOrDefault(r =>
                r.SummonerId == rank.SummonerId &&
                r.QueueType == rank.QueueType);

            if (existingRank != null)
            {
                // Check if the rank has actually changed
                bool rankChanged = existingRank.Tier != rank.Tier ||
                                 existingRank.RankNumber != rank.RankNumber ||
                                 existingRank.LeaguePoints != rank.LeaguePoints ||
                                 existingRank.Wins != rank.Wins ||
                                 existingRank.Losses != rank.Losses;

                if (rankChanged)
                {
                    // Check if we already have a historical rank for this exact state
                    var existingHistoricalRank = await context.HistoricalRanks
                        .OrderByDescending(hr => hr.DateRecorded)
                        .FirstOrDefaultAsync(hr =>
                            hr.Summoner.Id == rank.SummonerId &&
                            hr.QueueType == rank.QueueType &&
                            hr.Tier == existingRank.Tier &&
                            hr.RankNumber == existingRank.RankNumber &&
                            hr.LeaguePoints == existingRank.LeaguePoints &&
                            hr.Wins == existingRank.Wins &&
                            hr.Losses == existingRank.Losses,
                            cancellationToken);

                    if (existingHistoricalRank == null)
                    {
                        // Create a historical rank entry only if it's different
                        var historicalRank = new HistoricalRank
                        {
                            QueueType = existingRank.QueueType,
                            Tier = existingRank.Tier,
                            RankNumber = existingRank.RankNumber,
                            LeaguePoints = existingRank.LeaguePoints,
                            Wins = existingRank.Wins,
                            Losses = existingRank.Losses,
                            Summoner = existingRank.Summoner,
                            DateRecorded = DateTime.UtcNow
                        };

                        await context.HistoricalRanks.AddAsync(historicalRank, cancellationToken);
                    }

                    // Update existing rank properties
                    existingRank.Tier = rank.Tier;
                    existingRank.RankNumber = rank.RankNumber;
                    existingRank.LeaguePoints = rank.LeaguePoints;
                    existingRank.Wins = rank.Wins;
                    existingRank.Losses = rank.Losses;
                }
            }
            else
            {
                // Only add new ranks that don't exist
                await context.Ranks.AddAsync(rank, cancellationToken);
            }
        }
    }
    
}