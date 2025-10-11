namespace Transcendence.Data.Models.LoL.Static;

public class RuneVersion
{
    public int RuneId { get; set; }
    public string PatchVersion { get; set; }
    public string? Key { get; set; }
    public string Name { get; set; }
    public string? Description { get; set; }
    public int RunePathId { get; set; }
    public string? RunePathName { get; set; }
    public int Slot { get; set; }

    public Patch Patch { get; set; }
}
