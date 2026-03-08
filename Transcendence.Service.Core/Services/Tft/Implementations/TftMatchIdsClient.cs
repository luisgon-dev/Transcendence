using Camille.Enums;
using Camille.RiotGames;
using Transcendence.Service.Core.Services.RiotApi;
using Transcendence.Service.Core.Services.Tft.Interfaces;

namespace Transcendence.Service.Core.Services.Tft.Implementations;

public class TftMatchIdsClient(TftRiotApiContext riotApiContext) : ITftMatchIdsClient
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
        return await riotApiContext.Api.TftMatchV1()
            .GetMatchIdsByPUUIDAsync(regionalRoute, puuid, count, endTimeEpochSeconds, start, startTimeEpochSeconds, ct);
    }
}
