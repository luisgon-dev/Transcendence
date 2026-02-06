namespace Transcendence.Data.Models.LoL.Static;

public class ItemVersion
{
    public int ItemId { get; set; }
    public string PatchVersion { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = [];

    public Patch Patch { get; set; } = null!;
}
