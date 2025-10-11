using Transcendence.Data.Models.Service.Helpers;
namespace Transcendence.Data.Models.Service;

public class CurrentChampionLoadout
{
    public Guid Id { get; set; }

    public List<UnitWinPercent> UnitWinPercents { get; set; } = [];

    // filters for the loadout
    public string ChampionName { get; set; }
    public int ChampionId { get; set; }
    public string Lane { get; set; }
    public string Rank { get; set; }
    public string Patch { get; set; }
    public string QueueType { get; set; }
    public string Region { get; set; }
}
