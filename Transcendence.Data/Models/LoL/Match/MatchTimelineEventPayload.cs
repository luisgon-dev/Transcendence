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

    /// <summary>
    /// The three scalars the modeler's cohort scan reads, lifted out of <see cref="PayloadJson"/>.
    ///
    /// They are duplicated from the JSON on purpose. Extracting them at read time made that scan a
    /// full sequential pass over the whole 77 GB table -- Postgres cannot satisfy a jsonb expression
    /// from an index, so it read all 165M rows to keep the ~16% that are kill events, then ran six
    /// `->>` extractions on every surviving row. As real columns they can live in a partial covering
    /// index, which turns the same query into a ~2 GB index-only scan.
    ///
    /// Null means "absent from this event's payload", which is normal: BUILDING_KILL and
    /// ELITE_MONSTER_KILL carry no killerId, and an execution carries no killer team. Null does NOT
    /// mean "not yet backfilled" -- the backfill populates every kill-type row before the modeler is
    /// switched over, precisely so the reader never has to distinguish the two.
    /// </summary>
    public int? KillerId { get; set; }
    public int? KillerTeamId { get; set; }
    public int? TeamId { get; set; }

    public required Match Match { get; set; }
}
