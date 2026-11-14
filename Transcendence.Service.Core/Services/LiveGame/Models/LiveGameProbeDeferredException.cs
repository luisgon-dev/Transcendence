namespace Transcendence.Service.Core.Services.LiveGame.Models;

/// <summary>Temporary Riot backpressure; no live-game observation has been verified.</summary>
public sealed class LiveGameProbeDeferredException(
    string platformRegion, TimeSpan retryAfter, Exception? innerException = null)
    : Exception($"Live-game probe deferred by the {platformRegion} Riot rate budget.", innerException)
{
    public const string ErrorCode = "riot_rate_limited";
    public string PlatformRegion { get; } = platformRegion;
    public TimeSpan RetryAfter { get; } = TimeSpan.FromSeconds(Math.Clamp(retryAfter.TotalSeconds, 1, 600));
}
