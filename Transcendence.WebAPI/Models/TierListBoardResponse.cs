using System.Text.Json.Serialization;
using Transcendence.Service.Core.Services.Analytics.Models;

namespace Transcendence.WebAPI.Models;

public sealed record TierListBoardEntry : TierListEntry
{
    public TierListBoardEntry(TierListEntry entry, int rank) : base(entry)
    {
        Rank = rank;
        Grade = entry.Tier.ToString();
    }

    [JsonRequired]
    public int Rank { get; init; }

    [JsonRequired]
    public string Grade { get; init; }
}

public sealed record TierListBoardResponse(
    string Patch,
    string? Role,
    string? RankTier,
    string Region,
    [property: JsonRequired] IReadOnlyList<TierListBoardEntry> Entries,
    AnalyticsSampleMetadata? Sample,
    DateTime? ComputedAtUtc,
    string QueueFamily,
    TierScopeConfidence Confidence,
    AnalyticsPatchSelection? PatchSelection,
    [property: JsonRequired] int LowSampleCount,
    [property: JsonRequired] int TotalEntryCount)
{
    public static TierListBoardResponse From(TierListResponse scope, bool includeLowSample)
    {
        var entries = scope.Entries
            .Where(entry => includeLowSample || !entry.IsLowSample)
            .OrderBy(entry => entry.Tier)
            .Select((entry, index) => new TierListBoardEntry(entry, index + 1))
            .ToList();

        return new TierListBoardResponse(scope.Patch, scope.Role, scope.RankTier, scope.Region,
            entries, scope.Sample, scope.ComputedAtUtc, scope.QueueFamily, scope.Confidence, scope.PatchSelection,
            scope.Entries.Count(entry => entry.IsLowSample), scope.Entries.Count);
    }
}
