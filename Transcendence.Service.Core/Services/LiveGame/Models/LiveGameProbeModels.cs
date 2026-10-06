namespace Transcendence.Service.Core.Services.LiveGame.Models;

public sealed record LiveGameProbeOutcome(bool WasQueued, Guid OperationId, int RetryAfterSeconds);
