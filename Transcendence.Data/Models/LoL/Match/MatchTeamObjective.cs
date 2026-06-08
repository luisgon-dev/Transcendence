namespace Transcendence.Data.Models.LoL.Match;

/// <summary>
/// Per-team objective tallies for a match (one row per team: 100 / 200), from
/// Riot match-v5 info.teams[].objectives. Populated on new ingestion only.
/// </summary>
public class MatchTeamObjective
{
    public Guid MatchId { get; set; }
    public required Match Match { get; set; }
    public int TeamId { get; set; }

    public bool FirstBlood { get; set; }

    public int BaronKills { get; set; }
    public bool BaronFirst { get; set; }
    public int DragonKills { get; set; }
    public bool DragonFirst { get; set; }
    public int RiftHeraldKills { get; set; }
    public bool RiftHeraldFirst { get; set; }
    public int HordeKills { get; set; }
    public bool HordeFirst { get; set; }
    public int TowerKills { get; set; }
    public bool TowerFirst { get; set; }
    public int InhibitorKills { get; set; }
    public bool InhibitorFirst { get; set; }
}
