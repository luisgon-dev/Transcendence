using Camille.Enums;
using Camille.RiotGames;
using Transcendence.Service.Core.Services.RiotApi;
using Transcendence.Service.Core.Services.Jobs.Interfaces;

namespace Transcendence.Service.Core.Services.Jobs;

public sealed class RiotMatchIdsClient(LeagueRiotApiContext riotApiContext, IRiotRateGate rateGate) : IRiotMatchIdsClient
{
    public async Task<IReadOnlyList<string>?> GetMatchIdsByPuuidAsync(
        RegionalRoute regionalRoute,
        string puuid,
        int count,
        long? endTimeEpochSeconds,
        Queue? queue,
        long? startTimeEpochSeconds,
        int start,
        string? type,
        CancellationToken ct = default)
    {
        // An empty Riot page is a real end-of-window signal. Gate exhaustion is a deferral, so return
        // null and force callers to preserve their cursor rather than silently completing a backfill.
        if (!await rateGate.AcquireAsync(regionalRoute.ToString(), ct))
            return null;

        return await riotApiContext.Api.MatchV5()
            .GetMatchIdsByPUUIDAsync(
                regionalRoute,
                puuid,
                count,
                endTimeEpochSeconds,
                queue,
                startTimeEpochSeconds,
                start,
                type,
                ct);
    }
}
