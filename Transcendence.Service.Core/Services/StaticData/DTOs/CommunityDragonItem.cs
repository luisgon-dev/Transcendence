namespace Transcendence.Service.Core.StaticData.DTOs;

public class CommunityDragonItem
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    // Matches the CommunityDragon items.json field name
    public List<string> Categories { get; set; }
}
