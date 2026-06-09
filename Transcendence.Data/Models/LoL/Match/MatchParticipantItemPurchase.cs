namespace Transcendence.Data.Models.LoL.Match;

/// <summary>
/// Build-relevant category for an ordered timeline item purchase. Components, consumables,
/// trinkets and wards are dropped at ingestion time and never persisted.
/// </summary>
public enum BuildItemCategory
{
    Legendary = 0,
    Boots = 1,
    Starter = 2
}

/// <summary>
/// A single build-relevant item acquisition derived from the match timeline, net of
/// undo/sell/destroy events. Rows are stored in purchase order (<see cref="PurchaseIndex"/>)
/// per participant so the analytics layer can reconstruct the ordered build path with timing.
/// </summary>
public class MatchParticipantItemPurchase
{
    public Guid MatchId { get; set; }
    public int ParticipantId { get; set; }

    /// <summary>Zero-based position of this purchase among the participant's build-relevant acquisitions.</summary>
    public int PurchaseIndex { get; set; }

    public int ItemId { get; set; }

    /// <summary>Milliseconds from game start at which the item was completed/purchased.</summary>
    public int TimestampMs { get; set; }

    public BuildItemCategory Category { get; set; }

    public required Match Match { get; set; }
}
