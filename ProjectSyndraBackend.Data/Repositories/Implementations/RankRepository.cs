using Microsoft.EntityFrameworkCore;
using ProjectSyndraBackend.Data.Models.LoL.Account;
using ProjectSyndraBackend.Data.Repositories.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProjectSyndraBackend.Data.Repositories.Implementations
{
    public class RankRepository(ProjectSyndraContext context) : IRankRepository
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
                .Where(r => ranks.Select(rank => rank.SummonerId).Contains(r.SummonerId)).Include(rank => rank.Summoner)
                .ToListAsync(cancellationToken);

           // if they are different from the inputted rank, create a new historical rank , delete then update rank with the latest.
           // 

            if (existingRanks.Count == 0)
            {
                // No existing ranks, just add the new ranks
                await context.Ranks.AddRangeAsync(ranks, cancellationToken);
                await context.SaveChangesAsync(cancellationToken);
                return;
            }

            foreach (var rank in ranks)
            {
                var existingRank = existingRanks.FirstOrDefault(r => r.SummonerId == rank.SummonerId && r.QueueType == rank.QueueType);
                if (existingRank != null)
                {
                    // Create a historical rank entry
                    var historicalRank = new HistoricalRank
                    {
                        QueueType = existingRank.QueueType,
                        Tier = existingRank.Tier,
                        RankNumber = existingRank.RankNumber,
                        LeaguePoints = existingRank.LeaguePoints,
                        Wins = existingRank.Wins,
                        Losses = existingRank.Losses,
                        Summoner = existingRank.Summoner,
                    };
                    context.HistoricalRanks.Add(historicalRank);
                    context.Ranks.Remove(existingRank);
                }
                // Add or update the new rank
                context.Ranks.Add(rank);
            }
            await context.SaveChangesAsync(cancellationToken);

        }
    }
   
}
