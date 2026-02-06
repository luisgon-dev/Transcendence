using Transcendence.Data.Models.Service.Helpers;

namespace Transcendence.Data.Models.Service;

public class CurrentChampionLoadout
{
    public Guid Id { get; set; }

    public List<UnitWinPercent> UnitWinPercents { get; set; } = [];

    // filters for the loadout
    public string ChampionName { get; set; } = string.Empty;
    public int ChampionId { get; set; }
    public string Lane { get; set; } = string.Empty;
    public string Rank { get; set; } = string.Empty;
    public string Patch { get; set; } = string.Empty;
    public string QueueType { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
}
