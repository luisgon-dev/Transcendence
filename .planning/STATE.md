# Project State

## Project Reference

See: .planning/PROJECT.md (updated 2026-01-31)

**Core value:** Summoner profiles with comprehensive stats — the foundation that enables the desktop app to be built against this API

**Current focus:** Phase 4 - Live Game & Authentication (Desktop App Enablement)

## Current Position

Phase: 4 of 5 - Live Game & Authentication
Plan: 6 of 6 (Favorites + endpoint hardening)
Status: Phase complete
Last activity: 2026-02-05 - Completed 04-06-PLAN.md

Progress: ████████░░ 80% (4.0/5 phases complete)

## Performance Metrics

**Requirements:**
- Total v1 requirements: 21
- Requirements complete: 17 (INFRA-01, INFRA-02, PROF-01, PROF-02, PROF-03, PROF-04, CHAMP-01, CHAMP-02, CHAMP-03, CHAMP-04, LIVE-01, LIVE-02, LIVE-03, LIVE-04, AUTH-01, AUTH-02, AUTH-03)
- Requirements remaining: 4

**Phases:**
- Total phases: 5
- Phases complete: 4
- Current phase: Phase 5 (next)

**Velocity:**
- Plan 02-01: 15 minutes (3 tasks)
- Plan 02-02: 20 minutes (3 tasks)
- Plan 02-03: 15 minutes (3 tasks)
- Plan 02-04: 4 minutes (3 tasks, Task 1 pre-complete)
- Plan 03-01: 8 minutes (3 tasks)
- Plan 03-02: 4 minutes (3 tasks, Task 1 pre-complete)
- Plan 03-03: 7 minutes (3 tasks)
- Plan 03-04: 7 minutes (3 tasks, Tasks 1-2 pre-complete)
- Plan 03-05: 4 minutes (3 tasks)
- Plan 04-01: 20 minutes (3 tasks)
- Plan 04-02: 12 minutes (3 tasks)
- Plan 04-03: 14 minutes (3 tasks)
- Plan 04-04: 10 minutes (3 tasks)
- Plan 04-05: 18 minutes (3 tasks)
- Plan 04-06: 15 minutes (3 tasks)

## Recent Decisions

| Phase | Decision | Rationale |
|-------|----------|-----------|
| 03-05 | Daily refresh at 4 AM UTC | Low-traffic hours minimize user impact during cache invalidation (midnight EST, 9 PM PST) |
| 03-05 | Pre-warm top 20 champions per role | Pareto principle (80% traffic from 20% champions) balances coverage with execution time |
| 03-05 | Pre-warm Gold/Platinum/Emerald/Diamond tiers | Covers 60%+ of ranked player base, highest population density |
| 03-04 | 30-game minimum per matchup | Lower than 100-game champion minimum because matchup combinations are sparser |
| 03-04 | Counter threshold < 48%, Favorable > 52% | Creates clear separation from neutral 50% win rate matchups |
| 03-04 | Top 5 counters and top 5 favorable | Manageable list size for UI display while covering most common matchups |
| 03-04 | Lane-specific self-join (same role, different team) | Ensures Mid vs Mid comparisons, not Mid vs Top - accurate meta matchups |
| 03-03 | Flatten rune structure (PrimaryRunes, SubRunes, StatShards) | Simpler build grouping than nested DTO - previous RuneTreeDto structure would block build key generation |
| 03-03 | 30-game minimum per specific build | Balances statistical significance with availability - stricter than 100-game overall minimum |
| 03-03 | Build scoring via (games × winRate) | Simple composite balancing popularity with effectiveness for top 3 selection |
| 03-03 | Stat shards identified by RunePathId >= 5000 | Follows League's rune system convention - separate path from regular runes |
| 03-02 | Composite score 70% WR + 30% PR | Win rate primary indicator, pick rate provides meta relevance - prevents niche picks dominating S tier |
| 03-02 | Percentile-based tier thresholds | S=top 10%, A=10-30%, B=30-60%, C=60-85%, D=85%+ ensures tier distribution regardless of patch balance |
| 03-02 | Movement compares tier grades not ranks | Tier letter change more stable than rank comparison (rank 15→20 might be SAME tier) |
| 03-02 | Enums for TierGrade and TierMovement | Type safety prevents invalid values, better IDE support, consistent with .NET conventions |
| 03-02 | Separate AnalyticsController | /api/analytics/tierlist vs /api/analytics/champions/{id}/winrates - tier lists are meta-level vs champion-specific |
| 03-01 | 24hr L2 / 1hr L1 cache TTL for analytics | Longer than stats cache (5min) due to large dataset computation cost across thousands of matches |
| 03-01 | 100-game minimum threshold for win rates | Ensures statistical significance, prevents misleading data from small samples (from 03-RESEARCH user decision) |
| 03-01 | Two-tier analytics architecture | Compute service for raw EF queries, cached service wraps with HybridCache - clean separation, testability |
| 03-01 | Current patch only, no fallback | Keeps implementation simple, historical analytics can be added later if needed |
| 03-01 | Rank tier from RANKED_SOLO_5x5 queue | Solo queue is primary competitive mode for analytics |
| 02-04 | Items padded to 7 slots with 0s | MatchParticipantItem lacks Slot property - pad to consistent length (6 items + trinket) for UI |
| 02-04 | Rune summary vs full detail | Match cards show keystone+styles, full rune tree available via match detail endpoint |
| 02-04 | Batched queries for items/runes | 3 queries (participants, items, runes) prevents N+1 performance issues |
| 02-03 | 5-minute stats TTL (2min L1) | Shorter than profile/rank because stats aggregate from frequently-updated match data |
| 02-03 | 1-hour match detail TTL (15min L1) | Match data is immutable once stored, can cache longer |
| 02-03 | Eager invalidation of known cache keys | HybridCache lacks wildcard support - invalidate common parameter combinations on refresh |
| 02-03 | Extract ComputeXxx methods for cache factories | Keeps cache logic separate from business logic, follows GetOrCreateAsync pattern cleanly |
| 02-02 | Champion name placeholder "Champion {id}" | Phase 3 will add proper static data service for name resolution |
| 02-02 | StatsAge from most recent match date | FetchedAt uses first match MatchDate from RecentMatches for freshness indication |
| 02-02 | Task.WhenAll for parallel stats fetching | Minimizes latency by fetching overview, champions, recent concurrently |
| 02-01 | Rune style via RuneVersion lookup | Current MatchParticipantRune only stores RuneId - use RunePathId from static data to infer primary/sub |
| 02-01 | Flat item list without slots | Items stored deduplicated without slot positions, returned as simple list |
| 02-01 | Private RuneMetadata record | Type-safe alternative to dynamic for rune lookup results |
| 01 | L1 TTL (5min) shorter than L2 TTL (1hr) | Prevents stale-distributed-fresh scenarios where one server has stale in-memory cache but Redis has updated data |
| 01 | Use HybridCache built-in stampede protection | No manual locking needed - HybridCache guarantees only one concurrent caller executes factory for given key |
| 01 | CacheService wrapper abstraction | Centralizes cache key generation, improves testability, provides domain-specific API over infrastructure |
| 01 | 6-hour patch check interval | Satisfies requirement while minimizing API calls - patch cycle is ~2 weeks, hourly checks waste 95% of requests |
| 01 | 30-day cache TTL for static data | Outlives 2-week patch cycle, old patch data persists for historical queries |
| 01 | IsActive flag for current patch | Simplifies queries to FirstOrDefault(p => p.IsActive) instead of ordering by ReleaseDate |
| 01 | Tag-based cache invalidation | Cache entries tagged with patch version allow bulk invalidation on patch change |
| 01 | Exponential backoff delays: 30s, 60s, 120s, 300s | Balances responsiveness with API courtesy - first retry quick for transient failures, backs off if issue persists |
| 01 | Max 5 retry attempts before PermanentlyUnfetchable | Prevents infinite retry loops while being persistent - after ~10 minutes data likely permanently unavailable |
| 01 | 2-year retention window check before fetch | Riot API only retains match data for 2 years - checking before fetch prevents wasted API calls |
| 01 | Global query filter for PermanentlyUnfetchable | Unfetchable matches are historical records but shouldn't appear in normal queries - use IgnoreQueryFilters() for admin/reporting |
| 01 | Trust Camille SDK rate limiting | Camille parses X-Rate-Limit-* headers and respects Retry-After - custom throttling creates double-throttling |
| 01 | RetryFailedMatchesJob hourly as safety net | Catches edge cases from service restarts - primary retry is per-match exponential backoff |
| 01 | Separate ProfileAge and RankAge metadata | Profile data changes rarely, rank data changes frequently - different freshness expectations |
| 01 | Human-friendly age descriptions | Just now (<5 min), X minutes ago (<1 hr), X hours ago (<1 day), X days ago (>1 day) for UI display |
| 01 | UpdatedAt on Summoner entity | Added for ProfileAge tracking - was missing but required by plan |
| 01 | API contract change to SummonerProfileResponse | Acceptable in Phase 1 Foundation - no existing clients to break, desktop app will be built against this contract |

## Pending Todos

(None)

## Known Blockers

(None)

## Session Continuity

**Last session:** 2026-02-05
**Activity:** Phase 4 execution (plans 04-01 through 04-06)
**Stopped at:** Plan 04-06 complete, Phase 4 complete
**Resume file:** None

---

## Context for Next Session

**What we just did:**
- Completed all Phase 4 plans:
  - 04-01: API key authentication middleware + key management
  - 04-02: Spectator live game endpoint with cache
  - 04-03: Adaptive polling job + live snapshots
  - 04-04: Participant/team enrichment + win probability
  - 04-05: JWT register/login/refresh/password-reset-init
  - 04-06: User favorites/preferences + secured analytics invalidation endpoints

**Phase 4 summaries:**
- `.planning/phases/04-live-game-auth/04-01-SUMMARY.md`
- `.planning/phases/04-live-game-auth/04-02-SUMMARY.md`
- `.planning/phases/04-live-game-auth/04-03-SUMMARY.md`
- `.planning/phases/04-live-game-auth/04-04-SUMMARY.md`
- `.planning/phases/04-live-game-auth/04-05-SUMMARY.md`
- `.planning/phases/04-live-game-auth/04-06-SUMMARY.md`

**Phase 4 complete:**
- LIVE-01, LIVE-02, LIVE-03, LIVE-04 complete
- AUTH-01, AUTH-02, AUTH-03 complete

**Ready for:** Phase 5 - Management & Monitoring

---

*Last updated: 2026-02-05 21:27 UTC*
