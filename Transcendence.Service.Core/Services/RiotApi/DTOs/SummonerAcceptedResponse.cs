namespace Transcendence.Service.Core.Services.RiotApi.DTOs;

public record SummonerAcceptedResponse(
    string Message,
    string? Poll = null,
    int? RetryAfterSeconds = null
);

