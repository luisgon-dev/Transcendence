# Project State

## Project Reference

See: .planning/PROJECT.md (updated 2026-01-31)

**Core value:** Summoner profiles with comprehensive stats — the foundation that enables the desktop app to be built against this API

**Current focus:** Phase 2 - Summoner Profiles (Complete Profile API)

## Current Position

Phase: 2 of 5 - Summoner Profiles
Plan: 04 complete
Status: Ready for next phase or plan
Last activity: 2026-02-02 - Plan 02-04 complete (Match History Loadout)

Progress: ██░░░░░░░░ 20% (1/5 phases complete)

## Performance Metrics

**Requirements:**
- Total v1 requirements: 21
- Requirements complete: 2 (INFRA-01, INFRA-02)
- Requirements remaining: 19

**Phases:**
- Total phases: 5
- Phases complete: 1
- Current phase: Phase 2

**Velocity:**
- Plan 02-01: 15 minutes (3 tasks)
- Plan 02-02: 20 minutes (3 tasks)
- Plan 02-03: 15 minutes (3 tasks)
- Plan 02-04: 4 minutes (3 tasks, Task 1 pre-complete)

## Recent Decisions

| Phase | Decision | Rationale |
|-------|----------|-----------|
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

**Last session:** 2026-02-02
**Activity:** Plan 02-04 execution
**Stopped at:** Plan 02-04 complete
**Resume file:** None

---

## Context for Next Session

**What we just did:**
- Completed Plan 02-04 (Match History Loadout) with 3 tasks:
  - Task 1 already complete (RecentMatchSummary + MatchRuneSummary records)
  - Restructured ComputeRecentMatchesAsync for batched item/rune fetching
  - Updated controller DTOs and mapping to expose loadout data

**Key artifacts:**
- `Transcendence.Service.Core/Services/Analysis/Implementations/SummonerStatsService.cs` - Batched queries for items/runes
- `Transcendence.WebAPI/Models/Stats/SummonerStatsDtos.cs` - RecentMatchSummaryDto with loadout
- `Transcendence.WebAPI/Controllers/SummonerStatsController.cs` - Mapping updated

**Match history endpoint now returns:**
- Items array (7 slots: 6 items + trinket, 0 for empty)
- Rune summary (primary/sub style IDs, keystone ID)
- Summoner spell IDs (Spell1Id, Spell2Id)

**Query performance:**
- 3 batched queries (participants, items, runes) prevents N+1
- RuneVersion metadata fetched separately for efficient style inference
- Same pattern as match detail endpoint (consistent architecture)

**Ready for:** Phase 3 (Static Data Service) to add champion/item/rune name resolution

---

*Last updated: 2026-02-02*
