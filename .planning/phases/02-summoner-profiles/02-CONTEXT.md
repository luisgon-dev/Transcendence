# Phase 2: Summoner Profiles - Context

**Gathered:** 2026-02-02
**Status:** Ready for planning

<vision>
## How This Should Work

When a user looks up a summoner by Riot ID, they get everything instantly - the complete picture in one call. No progressive loading, no waiting for additional data. Type a name, get the full profile.

This is the foundation the desktop app will be built against. The profile experience should feel like instant access to everything you'd want to know about a player: their competitive standing, how they've been playing lately, and what champions they're best at.

</vision>

<essential>
## What Must Be Nailed

- **Speed** - Sub-500ms response time. If lookup feels slow, the whole app feels broken.
- **Completeness** - All three pillars present: ranked performance, recent form, champion mastery. A partial profile is a useless profile.
- **Accuracy** - Data must be fresh and correct. Stale data is worse than no data.

All three are non-negotiable. This is the core API that everything else builds on.

</essential>

<specifics>
## Specific Ideas

**Match history detail level:** Full stats per game - KDA, CS, damage, gold, items, runes, spells. Analytics-grade detail like op.gg, not just quick-glance summaries.

**Profile completeness means:**
- Current rank, LP, win/loss record (ranked performance)
- Last 20 games with full participant stats (recent form)
- Per-champion breakdown: games played, win rate, avg KDA (champion mastery)

</specifics>

<notes>
## Additional Context

Brownfield work - the codebase already has summoner lookup, match history, and ranked tracking. Phase 2 polishes these existing pieces into production-ready endpoints rather than building from scratch.

Phase 1 caching infrastructure (HybridCache, Redis L2) is already in place to support the speed requirement.

</notes>

---

*Phase: 02-summoner-profiles*
*Context gathered: 2026-02-02*
