# Project State

## Project Reference

See: .planning/PROJECT.md (updated 2026-01-31)

**Core value:** Summoner profiles with comprehensive stats — the foundation that enables the desktop app to be built against this API

**Current focus:** Phase 2 - Summoner Profiles (Complete Profile API)

## Current Position

Phase: 2 of 5 - Summoner Profiles
Plan: 01 complete
Status: Ready for Plan 02-02
Last activity: 2026-02-02 - Plan 02-01 complete (Match Details Endpoint)

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

## Recent Decisions

| Phase | Decision | Rationale |
|-------|----------|-----------|
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
**Activity:** Plan 02-01 execution
**Stopped at:** Plan 02-01 complete
**Resume file:** None

---

## Context for Next Session

**What we just did:**
- Completed Plan 02-01 (Match Details Endpoint) with 3 tasks:
  - Created MatchDetailDto with ParticipantDetailDto and ParticipantRunesDto
  - Implemented GetMatchDetailAsync in SummonerStatsService with EF Include chain
  - Added GET /api/summoners/{id}/matches/{matchId} endpoint

**Key artifacts:**
- `Transcendence.Service.Core/Services/RiotApi/DTOs/MatchDetailDto.cs` - Full match DTOs
- `SummonerStatsService.GetMatchDetailAsync()` - Match detail query
- `SummonerStatsController.GetMatchDetail()` - Match detail endpoint

**Ready for:** Plan 02-02 execution or next phase planning

---

*Last updated: 2026-02-02*
