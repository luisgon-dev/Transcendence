namespace Transcendence.Service.Core.Services.Jobs.Configuration;

public class RetryFailedMatchesJobOptions
{
    public int MaxMatchesPerRun { get; set; } = 20;
    public int MinimumMinutesSinceLastAttempt { get; set; } = 15;
}
