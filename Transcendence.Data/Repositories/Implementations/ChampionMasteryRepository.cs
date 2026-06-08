using Microsoft.EntityFrameworkCore;
using Transcendence.Data.Models.LoL.Account;
using Transcendence.Data.Repositories.Interfaces;

namespace Transcendence.Data.Repositories.Implementations;

public class ChampionMasteryRepository(TranscendenceContext context) : IChampionMasteryRepository
{
    public async Task UpsertAsync(Guid summonerId, List<ChampionMastery> masteries,
        CancellationToken cancellationToken = default)
    {
        var existing = await context.ChampionMasteries
            .Where(cm => cm.SummonerId == summonerId)
            .ToListAsync(cancellationToken);
        var existingByChampion = existing.ToDictionary(cm => cm.ChampionId);
        var incomingChampionIds = new HashSet<int>();

        foreach (var incoming in masteries)
        {
            incomingChampionIds.Add(incoming.ChampionId);

            if (existingByChampion.TryGetValue(incoming.ChampionId, out var current))
            {
                current.ChampionLevel = incoming.ChampionLevel;
                current.ChampionPoints = incoming.ChampionPoints;
                current.LastPlayTime = incoming.LastPlayTime;
                current.ChestGranted = incoming.ChestGranted;
                current.TokensEarned = incoming.TokensEarned;
                current.UpdatedAt = DateTime.UtcNow;
            }
            else
            {
                incoming.SummonerId = summonerId;
                incoming.UpdatedAt = DateTime.UtcNow;
                await context.ChampionMasteries.AddAsync(incoming, cancellationToken);
            }
        }

        // Prune masteries Riot no longer returns so the stored snapshot stays accurate.
        var stale = existing.Where(cm => !incomingChampionIds.Contains(cm.ChampionId)).ToList();
        if (stale.Count > 0)
            context.ChampionMasteries.RemoveRange(stale);
    }
}
