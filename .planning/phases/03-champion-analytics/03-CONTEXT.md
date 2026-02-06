# Phase 3: Champion Analytics - Context

**Gathered:** 2026-02-02
**Status:** Ready for planning

<domain>
## Phase Boundary

Aggregate match data into champion-level analytics: win rates by role/tier, optimal builds, tier list rankings, and matchup data. Users query these endpoints to inform champion selection and build decisions. This phase delivers read-only analytics endpoints — user-specific recommendations and live game integration are separate phases.

</domain>

<decisions>
## Implementation Decisions

### Filtering & Segmentation
- Rank tiers segmented individually (Iron, Bronze, Silver, Gold, Platinum, Emerald, Diamond, Master, Grandmaster, Challenger)
- Region filtering: global aggregation by default, per-region filter available as optional parameter
- Patch scope: current patch only (no fallback to previous patches)
- Minimum sample size: 100 games required to show analytics for a champion/role combination

### Tier List Criteria
- Tier grade determined by composite score: win rate + pick rate (not win rate alone)
- Tier lists show movement indicators (up/down arrows) from previous patch
- Both per-role tier lists (Top, Jungle, Mid, ADC, Support) and unified tier list available
- Per-role is the default view; unified available as alternative

### Build Recommendations
- Show top 3 builds per champion (not just single best build)
- Distinguish core items (appear in 70%+ of games) from situational items
- Include skill order (ability max order) in build recommendations

### Matchup Presentation
- Show both counters and synergies (not counters only)
- Matchups are lane-specific (Mid vs Mid, Top vs Top, etc.)
- Display game count for each matchup so users can judge reliability

### Claude's Discretion
- Tier boundary method (fixed percentile vs absolute thresholds) — choose based on data characteristics
- Whether runes are bundled with item builds or shown separately — decide based on rune/item correlation patterns
- Number of counters/synergies shown per champion — determine based on statistical significance thresholds

</decisions>

<specifics>
## Specific Ideas

No specific requirements — open to standard approaches for analytics presentation.

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope

</deferred>

---

*Phase: 03-champion-analytics*
*Context gathered: 2026-02-02*
