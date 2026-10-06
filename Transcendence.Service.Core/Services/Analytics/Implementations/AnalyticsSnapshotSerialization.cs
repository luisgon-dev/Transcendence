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
    public const string DatasetStatsFeature = "dataset-stats";

    /// <summary><c>ScopeKey</c> of the single dataset-stats row.</summary>
    public const string DatasetStatsScopeKey = "global";

    /// <summary>
    /// <c>Patch</c> value for a snapshot that does not belong to any patch. No real patch version can
    /// equal it, so per-patch deletes (<c>RefreshProSurfacesAsync</c>) never touch the row.
    /// </summary>
    public const string PatchIndependent = "*";

    /// <summary>Roster scopes precomputed for the pro surfaces (the <c>NormalizeProScope</c> tokens).</summary>
    public static readonly string[] ProScopes = ["all", "pro", "highelo"];

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string Serialize<T>(T response) => JsonSerializer.Serialize(response, Options);

    public static T? Deserialize<T>(string payload) => JsonSerializer.Deserialize<T>(payload, Options);
}
