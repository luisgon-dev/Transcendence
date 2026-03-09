using Microsoft.EntityFrameworkCore;
using Transcendence.Data.Models.Tft.Account;
using Transcendence.Data.Repositories.Interfaces;

namespace Transcendence.Data.Repositories.Implementations;

public class TftRankRepository(TranscendenceContext context) : ITftRankRepository
{
    public async Task AddOrUpdateRankAsync(
        TftSummoner summoner,
        IReadOnlyList<TftRank> incomingRanks,
        CancellationToken cancellationToken = default)
    {
        var existingRanks = await context.TftRanks
            .Where(x => x.SummonerId == summoner.Id)
            .ToListAsync(cancellationToken);

        foreach (var incoming in incomingRanks)
        {
            var existing = existingRanks.FirstOrDefault(x => x.QueueType == incoming.QueueType);
            if (existing == null)
            {
                incoming.Id = Guid.NewGuid();
                incoming.SummonerId = summoner.Id;
                incoming.Summoner = summoner;
                incoming.UpdatedAt = DateTime.UtcNow;
                context.TftRanks.Add(incoming);
                continue;
            }

            var changed = existing.Tier != incoming.Tier
                          || existing.RankNumber != incoming.RankNumber
                          || existing.LeaguePoints != incoming.LeaguePoints
                          || existing.Wins != incoming.Wins
                          || existing.Losses != incoming.Losses;

            if (!changed)
            {
                existing.UpdatedAt = DateTime.UtcNow;
                continue;
            }

            context.TftHistoricalRanks.Add(new TftHistoricalRank
            {
                Id = Guid.NewGuid(),
                SummonerId = summoner.Id,
                QueueType = existing.QueueType,
                Tier = existing.Tier,
                RankNumber = existing.RankNumber,
                LeaguePoints = existing.LeaguePoints,
                Wins = existing.Wins,
                Losses = existing.Losses,
                DateRecorded = DateTime.UtcNow,
                Summoner = summoner
            });

            existing.Tier = incoming.Tier;
            existing.RankNumber = incoming.RankNumber;
            existing.LeaguePoints = incoming.LeaguePoints;
            existing.Wins = incoming.Wins;
            existing.Losses = incoming.Losses;
            existing.UpdatedAt = DateTime.UtcNow;
        }
    }
}
