using System.ComponentModel;

namespace Transcendence.Service.Core.Services.RiotApi.DTOs;

public record SummonerAcceptedResponse(
    [property: Description(
        "Refresh state message. \"Refresh queued\" when accepted now, \"Refresh in process\" when contention is detected.")]
    string Message,
    [property: Description("Absolute URL clients can poll for current refresh status/result.")]
    string? Poll = null,
    [property: Description("Retry hint in seconds for contention responses; null when refresh was just queued.")]
    int? RetryAfterSeconds = null
);

