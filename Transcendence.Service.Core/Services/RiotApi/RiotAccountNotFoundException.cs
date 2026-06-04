namespace Transcendence.Service.Core.Services.RiotApi;

/// <summary>
/// Thrown when the Riot Account API cannot resolve a PUUID for a given riot id
/// (the account was deleted, or the gameName#tagLine was changed/reassigned).
/// This is a permanent condition for that riot id, so refresh jobs should skip it
/// rather than fail and retry indefinitely.
/// </summary>
public sealed class RiotAccountNotFoundException(string message) : InvalidOperationException(message);
