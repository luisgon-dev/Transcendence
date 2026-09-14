using System.Text.Json.Serialization;
using Camille.Enums;

namespace Transcendence.Service.Core.Services.RiotApi.Interfaces;

/// <summary>
/// League-V4 ranked entries for one account, deserialized without Camille's <c>QueueType</c> enum.
/// </summary>
/// <remarks>
/// <para>
/// Riot treats <c>queueType</c> as an open vocabulary and extends it without updating the published
/// schema Camille generates from. As of 2026-09 that schema still lists only eight queue types, while
/// prod has ingested <c>RANKED_PREMADE_5x5</c> and <c>JADE_RANKED_SOLO_5x5</c> — neither modelled.
/// Camille's generated enum is a closed set behind a plain <c>JsonStringEnumConverter</c>, so one
/// unmodelled entry threw and failed the whole array.
/// </para>
/// <para>
/// Upstream hit the same wall and added an <c>UNKNOWN</c> fallback (MingweiSamuel/Camille 0e97b7d,
/// 2026-08-22) rather than chasing the values, but it is unreleased — Camille's CI has been failing
/// since early 2026 and is now disabled by GitHub for inactivity, so the newest NuGet build is still
/// 2025-06-24. That fallback would also collapse both real queues to <c>UNKNOWN</c>, discarding names
/// this system stores verbatim. Keeping the field a string is what actually ends the treadmill.
/// </para>
/// </remarks>
public interface ILeagueEntriesClient
{
    Task<IReadOnlyList<LeagueEntryRaw>> GetLeagueEntriesAsync(
        string summonerPuuid, PlatformRoute platformRoute, CancellationToken cancellationToken = default);
}

/// <summary>
/// Mirrors the League-V4 entry fields this system stores. Every value Riot controls the vocabulary of
/// stays a string, matching the <c>Rank</c> columns they are written to, so no value can fail to bind.
/// Property names are explicit because Camille's serializer binds its own models by attribute.
/// </summary>
public sealed record LeagueEntryRaw(
    [property: JsonPropertyName("queueType")] string? QueueType,
    [property: JsonPropertyName("tier")] string? Tier,
    [property: JsonPropertyName("rank")] string? Rank,
    [property: JsonPropertyName("leaguePoints")] int LeaguePoints,
    [property: JsonPropertyName("wins")] int Wins,
    [property: JsonPropertyName("losses")] int Losses);
