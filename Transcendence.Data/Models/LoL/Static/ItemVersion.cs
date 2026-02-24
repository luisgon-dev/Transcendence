namespace Transcendence.Data.Models.LoL.Static;

public class ItemVersion
{
    public int ItemId { get; set; }
    public string PatchVersion { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = [];
    public List<int> BuildsFrom { get; set; } = [];
    public List<int> BuildsInto { get; set; } = [];
    public bool InStore { get; set; } = true;
    public int PriceTotal { get; set; }

    public Patch Patch { get; set; } = null!;
}
