namespace Transcendence.Data.Models.LoL.Match;

/// <summary>
/// Sanctioned Match-V5 timeline payloads for the event types offline analytics actually consumes:
/// the item lifecycle (purchase/sell/undo/destroy) plus CHAMPION_KILL, BUILDING_KILL and
/// ELITE_MONSTER_KILL. Every other event type is dropped at ingestion, and null members of the
/// serialized event union are omitted, so the JSON is not a byte-for-byte copy of Riot's response.
/// The indexed envelope supports type- and time-bounded reads while JSON preserves the fields an
/// event actually carries without coupling the durable schema to Camille's generated event model.
/// </summary>
public class MatchTimelineEventPayload
{
    public Guid MatchId { get; set; }
    public int EventIndex { get; set; }
    public int TimestampMs { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = "{}";

    public required Match Match { get; set; }
}
