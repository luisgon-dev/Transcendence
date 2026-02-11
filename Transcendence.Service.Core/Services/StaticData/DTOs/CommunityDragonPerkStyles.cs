using System.Text.Json.Serialization;

namespace Transcendence.Service.Core.Services.StaticData.DTOs;

public class CommunityDragonPerkStylesRoot
{
    [JsonPropertyName("schemaVersion")] public int SchemaVersion { get; set; }
    [JsonPropertyName("styles")] public List<CommunityDragonPerkStyle> Styles { get; set; } = [];
}

public class CommunityDragonPerkStyle
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("iconPath")] public string IconPath { get; set; } = string.Empty;
    [JsonPropertyName("slots")] public List<CommunityDragonPerkStyleSlot> Slots { get; set; } = [];
}

public class CommunityDragonPerkStyleSlot
{
    [JsonPropertyName("type")] public string Type { get; set; } = string.Empty;
    [JsonPropertyName("perks")] public List<int> Perks { get; set; } = [];
}
