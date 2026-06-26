using System.Text.Json;

namespace Transcendence.Service.Core.Services.Analytics.Implementations;

/// <summary>
/// Single source of truth for serializing/deserializing a response DTO to/from an
/// <c>AnalyticsResponseSnapshot.Payload</c>. The refresh (serialize) and the read (deserialize) MUST use the
/// same options so the round-trip is exact.
/// </summary>
internal static class AnalyticsSnapshotSerialization
{
    /// <summary><c>AnalyticsResponseSnapshot.Feature</c> values.</summary>
    public const string ProBuildsFeature = "probuilds";
    public const string ProPlayrateFeature = "proplayrate";

    /// <summary>Roster scopes precomputed for the pro surfaces (the <c>NormalizeProScope</c> tokens).</summary>
    public static readonly string[] ProScopes = ["all", "pro", "highelo"];

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string Serialize<T>(T response) => JsonSerializer.Serialize(response, Options);

    public static T? Deserialize<T>(string payload) => JsonSerializer.Deserialize<T>(payload, Options);
}
