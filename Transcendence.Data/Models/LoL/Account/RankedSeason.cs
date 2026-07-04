namespace Transcendence.Data.Models.LoL.Account;

public class RankedSeason
{
    public string SeasonKey { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public DateTime StartUtc { get; set; }
    public DateTime? EndUtc { get; set; }
    public bool IsActive { get; set; }
}
