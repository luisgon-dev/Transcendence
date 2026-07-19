using Camille.Enums;
using Transcendence.Data.Models.LoL.Account;

namespace Transcendence.Service.Core.Services.RiotApi.Interfaces;

/// <summary>
/// Tolerant fallback for League-V4 ranked entries. Camille deserializes each entry's <c>queueType</c>
/// into its <c>Camille.Enums.QueueType</c> enum and throws on any value it does not model — and Riot can
/// introduce queue types faster than Camille's (already-latest) nightly ships them, which fails the whole
/// array. This path re-fetches the same endpoint and parses <c>queueType</c> as a plain string, so an
/// account keeps its Solo/Flex rank instead of being dropped when it also carries an unknown-queue entry.
/// </summary>
public interface IRankFallbackClient
{
    Task<List<Rank>> GetLeagueEntriesTolerantAsync(
        string summonerPuuid, PlatformRoute platformRoute, CancellationToken cancellationToken = default);
}
