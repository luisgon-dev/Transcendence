namespace Transcendence.Service.Core.Services.StaticData.DTOs;

public class CommunityDragonItem
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    // Matches the CommunityDragon items.json field name
    public List<string> Categories { get; set; } = [];
}
