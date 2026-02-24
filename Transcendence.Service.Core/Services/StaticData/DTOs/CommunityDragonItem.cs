namespace Transcendence.Service.Core.Services.StaticData.DTOs;

public class CommunityDragonItem
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    // Matches the CommunityDragon items.json field name
    public List<string> Categories { get; set; } = [];

    // Recipe relationships in CommunityDragon items.json
    public List<int> From { get; set; } = [];
    public List<int> To { get; set; } = [];

    public bool? InStore { get; set; }
    public int? PriceTotal { get; set; }
}
