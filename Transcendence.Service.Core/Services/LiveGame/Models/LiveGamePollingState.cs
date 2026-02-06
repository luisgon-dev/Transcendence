namespace Transcendence.Service.Core.Services.LiveGame.Models;

public static class LiveGamePollingState
{
    public static TimeSpan GetNextInterval(string state)
    {
        return state switch
        {
            "lobby" => TimeSpan.FromSeconds(30),
            "in_game" => TimeSpan.FromSeconds(60),
            "ended" => TimeSpan.FromSeconds(30),
            _ => TimeSpan.FromMinutes(5)
        };
    }
}
