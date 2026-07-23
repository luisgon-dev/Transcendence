using System.ComponentModel;

namespace Transcendence.Service.Core.Services.RiotApi.DTOs;

public static class SummonerLookupStatuses
{
    public const string Ready = "ready";
    public const string Refreshing = "refreshing";
    public const string Missing = "missing";
}

public sealed record SummonerLookupResponse(
    [property: Description("Lookup state: ready, refreshing, or missing.")]
    string Status,
    [property: Description("Summoner profile when status is ready; otherwise null.")]
    SummonerProfileResponse? Profile = null,
    [property: Description("Human-readable state detail when the profile is not ready.")]
    string? Message = null,
    [property: Description("Absolute URL clients can poll for the current lookup state.")]
    string? Poll = null,
    [property: Description("Suggested delay before polling again when status is refreshing.")]
    int? RetryAfterSeconds = null
);
