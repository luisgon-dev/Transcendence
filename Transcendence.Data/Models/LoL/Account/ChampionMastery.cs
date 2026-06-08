using System.Text.Json.Serialization;

namespace Transcendence.Data.Models.LoL.Account;

/// <summary>
/// A summoner's champion-mastery snapshot (Riot mastery-v4). One row per
/// (summoner, champion). Refreshed by the worker on summoner refresh.
/// </summary>
public class ChampionMastery
{
    public Guid SummonerId { get; set; }
    public int ChampionId { get; set; }
    public int ChampionLevel { get; set; }
    public long ChampionPoints { get; set; }
    public long LastPlayTime { get; set; }
    public bool ChestGranted { get; set; }
    public int TokensEarned { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [JsonIgnore] public Summoner? Summoner { get; set; }
}
