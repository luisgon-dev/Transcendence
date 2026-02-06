using Microsoft.EntityFrameworkCore;
using Transcendence.Data.Models.LiveGame;
using Transcendence.Data.Repositories.Interfaces;

namespace Transcendence.Data.Repositories.Implementations;

public class LiveGameSnapshotRepository(TranscendenceContext db) : ILiveGameSnapshotRepository
{
    public Task<LiveGameSnapshot?> GetLatestByPuuidAsync(
        string puuid,
        string platformRegion,
        CancellationToken ct = default)
    {
        return db.Set<LiveGameSnapshot>()
            .AsNoTracking()
            .Where(x => x.Puuid == puuid && x.PlatformRegion == platformRegion)
            .OrderByDescending(x => x.ObservedAtUtc)
            .FirstOrDefaultAsync(ct);
    }

    public async Task AddAsync(LiveGameSnapshot snapshot, CancellationToken ct = default)
    {
        await db.Set<LiveGameSnapshot>().AddAsync(snapshot, ct);
    }

    public Task SaveChangesAsync(CancellationToken ct = default)
    {
        return db.SaveChangesAsync(ct);
    }
}
