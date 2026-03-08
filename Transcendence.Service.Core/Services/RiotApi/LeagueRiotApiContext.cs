using Camille.RiotGames;

namespace Transcendence.Service.Core.Services.RiotApi;

public sealed class LeagueRiotApiContext(RiotGamesApi api)
{
    public RiotGamesApi Api { get; } = api;
}
