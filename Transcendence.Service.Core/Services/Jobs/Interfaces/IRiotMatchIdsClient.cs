using Camille.Enums;

namespace Transcendence.Service.Core.Services.Jobs.Interfaces;

public interface IRiotMatchIdsClient
{
    Task<IReadOnlyList<string>> GetMatchIdsByPuuidAsync(
        RegionalRoute regionalRoute,
        string puuid,
        int count,
        long? endTimeEpochSeconds,
        Queue? queue,
        long? startTimeEpochSeconds,
        int start,
        string? type,
        CancellationToken ct = default);
}
