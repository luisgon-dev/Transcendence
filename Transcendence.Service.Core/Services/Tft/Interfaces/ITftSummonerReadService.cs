using Transcendence.Service.Core.Services.Tft.Models;

namespace Transcendence.Service.Core.Services.Tft.Interfaces;

public interface ITftSummonerReadService
{
    Task<IReadOnlyList<(string PlatformRegion, string Region, string GameName, string TagLine, int ProfileIconId)>> SearchAsync(
        string region,
        string query,
        int limit,
        CancellationToken ct = default);

    Task<TftSummonerProfileDto?> GetProfileByRiotIdAsync(
        string platformRegion,
        string gameName,
        string tagLine,
        CancellationToken ct = default);

    Task<TftPagedMatchesDto> GetRecentMatchesAsync(
        Guid summonerId,
        int page,
        int pageSize,
        CancellationToken ct = default);

    Task<TftMatchDetailDto?> GetMatchDetailAsync(Guid summonerId, string matchId, CancellationToken ct = default);
}
