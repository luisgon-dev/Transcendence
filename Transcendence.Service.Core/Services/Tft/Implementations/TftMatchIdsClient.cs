using Camille.Enums;
using Camille.RiotGames;
using Transcendence.Service.Core.Services.RiotApi;
using Transcendence.Service.Core.Services.Tft.Interfaces;

namespace Transcendence.Service.Core.Services.Tft.Implementations;

public class TftMatchIdsClient(TftRiotApiContext riotApiContext, IRiotRateGate rateGate) : ITftMatchIdsClient
{
    public async Task<IReadOnlyList<string>> GetMatchIdsByPuuidAsync(
        RegionalRoute regionalRoute,
        string puuid,
        int count,
        long? endTimeEpochSeconds,
        int? start,
        long? startTimeEpochSeconds,
        CancellationToken ct = default)
    {
        // Pace under the per-region Riot budget (shared gate with the LoL vertical). An empty list means
        // "no new ids this run" to the caller, which simply ends paging — safe when the region's budget is
        // momentarily exhausted past the gate's max wait.
        if (!await rateGate.AcquireAsync(regionalRoute.ToString(), ct))
            return [];

        return await riotApiContext.Api.TftMatchV1()
            .GetMatchIdsByPUUIDAsync(regionalRoute, puuid, count, endTimeEpochSeconds, start, startTimeEpochSeconds, ct);
    }
}
