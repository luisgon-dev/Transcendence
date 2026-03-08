using Transcendence.Data.Models.Tft.Account;

namespace Transcendence.Data.Models.Tft.Match;

public class TftMatchParticipant
{
    public Guid Id { get; set; }
    public Guid MatchId { get; set; }
    public Guid SummonerId { get; set; }
    public string Puuid { get; set; } = string.Empty;
    public string? RiotIdGameName { get; set; }
    public string? RiotIdTagLine { get; set; }
    public int Placement { get; set; }
    public int Level { get; set; }
    public int LastRound { get; set; }
    public int PlayersEliminated { get; set; }
    public int GoldLeft { get; set; }
    public int TotalDamageToPlayers { get; set; }
    public float TimeEliminatedSeconds { get; set; }
    public bool? Win { get; set; }
    public string[] Augments { get; set; } = [];
    public int? CompanionItemId { get; set; }
    public int? CompanionSkinId { get; set; }
    public string? CompanionSpecies { get; set; }

    public required TftMatch Match { get; set; }
    public required TftSummoner Summoner { get; set; }
    public ICollection<TftMatchParticipantUnit> Units { get; set; } = new List<TftMatchParticipantUnit>();
    public ICollection<TftMatchParticipantTrait> Traits { get; set; } = new List<TftMatchParticipantTrait>();
    public ICollection<TftMatchParticipantAugment> AugmentRows { get; set; } = new List<TftMatchParticipantAugment>();
}
