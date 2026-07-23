using Camille.Enums;

namespace Transcendence.Service.Core.Services.Jobs.Interfaces;

public interface IRiotMatchIdsClient
{
    /// <summary>
    /// Returns a page of match ids. An empty page means Riot has no more ids for the requested
    /// window; <see langword="null"/> means the regional rate gate deferred the request and the
    /// caller must preserve its cursor/state and retry later.
    /// </summary>
    Task<IReadOnlyList<string>?> GetMatchIdsByPuuidAsync(
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
