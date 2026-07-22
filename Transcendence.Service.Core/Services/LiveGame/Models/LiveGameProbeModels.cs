namespace Transcendence.Service.Core.Services.LiveGame.Models;

public sealed record LiveGameProbeOutcome(bool WasQueued, int RetryAfterSeconds);

public sealed record LiveGameProbeAcceptedResponse(
    string Status,
    string? Poll,
    int RetryAfterSeconds);
