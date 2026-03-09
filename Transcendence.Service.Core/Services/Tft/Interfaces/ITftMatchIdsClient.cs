using Camille.Enums;

namespace Transcendence.Service.Core.Services.Tft.Interfaces;

public interface ITftMatchIdsClient
{
    Task<IReadOnlyList<string>> GetMatchIdsByPuuidAsync(
        RegionalRoute regionalRoute,
        string puuid,
        int count,
        long? endTimeEpochSeconds,
        int? start,
        long? startTimeEpochSeconds,
        CancellationToken ct = default);
}
