namespace Transcendence.Service.Core.Services.RiotApi;

/// <summary>Temporary regional backpressure, not a missing or failed match.</summary>
public sealed class MatchPreparationDeferredException()
    : Exception("Match preparation was deferred by the regional Riot rate budget.");
