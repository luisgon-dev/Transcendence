using Transcendence.Data.Models.LiveGame;

namespace Transcendence.Data.Repositories.Interfaces;

public interface ILiveGameSnapshotRepository
{
    Task<LiveGameSnapshot?> GetLatestByPuuidAsync(string puuid, string platformRegion, CancellationToken ct = default);
    Task AddAsync(LiveGameSnapshot snapshot, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
