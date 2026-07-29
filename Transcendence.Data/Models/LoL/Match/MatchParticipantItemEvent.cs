namespace Transcendence.Data.Models.LoL.Match;

public enum MatchItemEventType
{
    Purchased = 0,
    Sold = 1,
    Undo = 2,
    Destroyed = 3
}

/// <summary>
/// Lossless analytics projection of Riot item lifecycle events. Unlike
/// <see cref="MatchParticipantItemPurchase"/>, these rows retain components and reversals so a
/// model can reconstruct the inventory immediately before every purchase decision.
/// </summary>
public class MatchParticipantItemEvent
{
    public Guid MatchId { get; set; }
    public int ParticipantId { get; set; }
    public int EventIndex { get; set; }
    public MatchItemEventType EventType { get; set; }
    public int TimestampMs { get; set; }
    public int? ItemId { get; set; }
    public int? BeforeId { get; set; }
    public int? AfterId { get; set; }
    public bool IsBuildRelevant { get; set; }
    public BuildItemCategory? BuildCategory { get; set; }

    public required Match Match { get; set; }
}
