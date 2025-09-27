using System;
using Transcendence.Data.Models.LoL.Account;

namespace Transcendence.Data.Models.LoL.Match;

public class MatchParticipant
{
    public Guid Id { get; set; }

    // Foreign keys
    public Guid MatchId { get; set; }
    public required Match Match { get; set; }

    public Guid SummonerId { get; set; }
    public required Summoner Summoner { get; set; }

    // Core identifiers
    public string? Puuid { get; set; } // denormalized for quick joins/filtering when needed
    public int TeamId { get; set; } // 100 or 200
    public int ChampionId { get; set; }

    // Position/role
    public string? TeamPosition { get; set; } // TOP/JUNGLE/MIDDLE/BOTTOM/UTILITY or reported position

    // Outcome
    public bool Win { get; set; }

    // Basic combat/economy
    public int Kills { get; set; }
    public int Deaths { get; set; }
    public int Assists { get; set; }
    public int ChampLevel { get; set; }
    public int GoldEarned { get; set; }
    public int TotalDamageDealtToChampions { get; set; }
    public int VisionScore { get; set; }
    public int TotalMinionsKilled { get; set; }
    public int NeutralMinionsKilled { get; set; }

    // Spells
    public int SummonerSpell1Id { get; set; }
    public int SummonerSpell2Id { get; set; }

    public Runes? Runes { get; set; }
    
    // Items (0-6 + trinket)
    public int Item0 { get; set; }
    public int Item1 { get; set; }
    public int Item2 { get; set; }
    public int Item3 { get; set; }
    public int Item4 { get; set; }
    public int Item5 { get; set; }
    public int Item6 { get; set; } // often control wards/consumables
    public int TrinketItem { get; set; }
}
