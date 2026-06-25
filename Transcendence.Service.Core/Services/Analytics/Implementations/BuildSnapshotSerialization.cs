using System.Text.Json;
using Transcendence.Service.Core.Services.Analytics.Models;

namespace Transcendence.Service.Core.Services.Analytics.Implementations;

/// <summary>
/// Single source of truth for serializing/deserializing a <see cref="ChampionBuildsResponse"/> to/from a
/// <c>ChampionBuildSnapshot.Payload</c>. The refresh (serialize) and the read (deserialize) MUST use the
/// same options so the round-trip is exact.
/// </summary>
internal static class BuildSnapshotSerialization
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string Serialize(ChampionBuildsResponse response) =>
        JsonSerializer.Serialize(response, Options);

    public static ChampionBuildsResponse? Deserialize(string payload) =>
        JsonSerializer.Deserialize<ChampionBuildsResponse>(payload, Options);
}
