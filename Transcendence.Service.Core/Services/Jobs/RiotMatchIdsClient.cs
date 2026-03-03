using Camille.Enums;
using Camille.RiotGames;
using Transcendence.Service.Core.Services.Jobs.Interfaces;

namespace Transcendence.Service.Core.Services.Jobs;

public sealed class RiotMatchIdsClient(RiotGamesApi riotGamesApi) : IRiotMatchIdsClient
{
    public async Task<IReadOnlyList<string>> GetMatchIdsByPuuidAsync(
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
        return await riotGamesApi.MatchV5()
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
