using Camille.Enums;
using Transcendence.Service.Core.Services.RiotApi.Interfaces;

namespace Transcendence.Service.Core.Services.RiotApi.Implementations;

/// <summary>
/// Fetches League-V4 entries over Camille's own transport, deserializing into <see cref="LeagueEntryRaw"/>
/// instead of its generated <c>LeagueEntry</c>.
/// </summary>
/// <remarks>
/// This is deliberately Camille's public <c>Send&lt;T&gt;</c> and not a bespoke <see cref="HttpClient"/>:
/// it keeps the adaptive app/method rate limiting, the 429 and 5xx retries and the platform-host routing
/// that every other Camille call gets. The route and method id are exactly what
/// <c>LeagueV4Endpoints.GetLeagueEntriesByPUUIDAsync</c> passes, so this shares that endpoint's method
/// rate-limit bucket rather than opening a second, unaccounted one.
///
/// Only the response model differs, because <c>queueType</c> is the one field Camille cannot keep up with
/// (see <see cref="ILeagueEntriesClient"/>). Nothing here is a second code path: this is the only way rank
/// is read, so there is no typed attempt to fail first and no duplicate request to pay for.
/// </remarks>
public sealed class CamilleLeagueEntriesClient(LeagueRiotApiContext riotApiContext) : ILeagueEntriesClient
{
    public async Task<IReadOnlyList<LeagueEntryRaw>> GetLeagueEntriesAsync(
        string summonerPuuid, PlatformRoute platformRoute, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get, "/lol/league/v4/entries/by-puuid/" + Uri.EscapeDataString(summonerPuuid));

        // Camille yields null rather than throwing when Riot answers 404 (account with no ranked entries).
        var entries = await riotApiContext.Api.Send<LeagueEntryRaw[]>(
            platformRoute.ToString(), "league-v4.getLeagueEntriesByPUUID", request, cancellationToken);

        return entries ?? [];
    }
}
