using System.Text.Json.Serialization;

namespace Transcendence.Service.Core.Services.StaticData.DTOs;

public class CommunityDragonRune
{
    [JsonPropertyName("id")] public int Id { get; set; }

    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;

    [JsonPropertyName("majorChangePatchVersion")]
    public string MajorChangePatchVersion { get; set; } = string.Empty;

    [JsonPropertyName("tooltip")] public string Tooltip { get; set; } = string.Empty;

    [JsonPropertyName("shortDesc")] public string ShortDesc { get; set; } = string.Empty;

    [JsonPropertyName("longDesc")] public string LongDesc { get; set; } = string.Empty;

    [JsonPropertyName("recommendationDescriptor")]
    public string RecommendationDescriptor { get; set; } = string.Empty;

    [JsonPropertyName("iconPath")] public string IconPath { get; set; } = string.Empty;

    [JsonPropertyName("endOfGameStatDescs")]
    public List<string> EndOfGameStatDescs { get; set; } = [];
}
