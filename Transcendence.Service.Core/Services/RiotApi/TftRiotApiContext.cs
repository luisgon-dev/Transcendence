using Camille.RiotGames;

namespace Transcendence.Service.Core.Services.RiotApi;

public sealed class TftRiotApiContext(RiotGamesApi api)
{
    public RiotGamesApi Api { get; } = api;
}
