namespace Transcendence.Data.Models.LoL.Static;

public class ItemVersion
{
    public int ItemId { get; set; }
    public string PatchVersion { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public List<string> Tags { get; set; }

    public Patch Patch { get; set; }
}
