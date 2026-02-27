using Camille.Enums;
using Camille.RiotGames;
using Camille.RiotGames.Util;
using Transcendence.Service.Core.Services.RiotApi.Interfaces;

namespace Transcendence.Service.Core.Services.RiotApi.Implementations;

public class RiotAccountService(RiotGamesApi riotApi) : IRiotAccountService
{
    public async Task<string?> ResolvePuuidAsync(string gameName, string tagLine, PlatformRoute platform,
        CancellationToken ct = default)
    {
        try
        {
            var account = await riotApi.AccountV1()
                .GetByRiotIdAsync(platform.ToRegional(), gameName, tagLine, ct);
            return account?.Puuid;
        }
        catch (RiotResponseException ex) when (ex.GetResponse()?.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }
}
