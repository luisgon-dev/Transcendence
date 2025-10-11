using System.Text.Json.Serialization;
namespace Transcendence.Service.Services.StaticData.DTOs;

public class CommunityDragonRune
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; }

    [JsonPropertyName("majorChangePatchVersion")]
    public string MajorChangePatchVersion { get; set; }

    [JsonPropertyName("tooltip")]
    public string Tooltip { get; set; }

    [JsonPropertyName("shortDesc")]
    public string ShortDesc { get; set; }

    [JsonPropertyName("longDesc")]
    public string LongDesc { get; set; }

    [JsonPropertyName("recommendationDescriptor")]
    public string RecommendationDescriptor { get; set; }

    [JsonPropertyName("iconPath")]
    public string IconPath { get; set; }

    [JsonPropertyName("endOfGameStatDescs")]
    public List<string> EndOfGameStatDescs { get; set; }
}
