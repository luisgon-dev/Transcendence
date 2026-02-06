---
phase: 02-summoner-profiles
plan: 03
subsystem: caching
tags: [HybridCache, Redis, performance, stats]

# Dependency graph
requires:
  - phase: 01-foundation
    provides: "HybridCache infrastructure with L1/L2 distributed caching"
  - phase: 02-summoner-profiles
    provides: "Stats calculation methods in SummonerStatsService"
provides:
  - "Cached stats queries with sub-500ms response times"
  - "Automatic cache invalidation on summoner refresh"
  - "Tiered TTLs: 5min for stats (mutable), 1hr for match details (immutable)"
affects:
  - "All profile endpoints benefit from cached stats queries"
  - "Future stats endpoints should follow same caching pattern"

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "GetOrCreateAsync pattern for cache-aside with HybridCache"
    - "Cache invalidation after data mutation (refresh job)"
    - "TTL tuning based on data mutability (5min stats vs 1hr match details)"

key-files:
  created: []
  modified:
    - "Transcendence.Service.Core/Services/Analysis/Implementations/SummonerStatsService.cs"
    - "Transcendence.Service.Core/Services/Jobs/SummonerRefreshJob.cs"

key-decisions:
  - "5-minute stats TTL (2min L1) - shorter than profile/rank because stats aggregate from matches"
  - "1-hour match detail TTL (15min L1) - match data is immutable once stored"
  - "Eager invalidation of common cache keys on refresh - no wildcard support in HybridCache"
  - "Invalidate known parameter combinations (top 5/10 champions, pages 1-3, etc.)"

patterns-established:
  - "Extract compute methods (ComputeOverviewAsync, etc.) for clean cache wrapping"
  - "Cache key format: stats:{type}:{summonerId}:{params}"
  - "Invalidation after successful mutation (matches saved → invalidate stats)"

# Metrics
duration: 15min
completed: 2026-02-02
---

# Phase 02 Plan 03: Stats Query Caching Summary

**HybridCache applied to all stats queries with 5-minute TTL and automatic invalidation on refresh for sub-500ms profile responses**

## Performance

- **Duration:** 15 min
- **Started:** 2026-02-02T10:06:30-08:00
- **Completed:** 2026-02-02T14:46:57-08:00
- **Tasks:** 3
- **Files modified:** 2

## Accomplishments
- All stats query methods wrapped with HybridCache GetOrCreateAsync
- Cache keys include summonerId to prevent cross-user pollution
- Tiered TTL strategy: 5min for mutable stats, 1hr for immutable match details
- Automatic cache invalidation when summoner refresh completes

## Task Commits

Each task was committed atomically:

1. **Task 1 & 2: Add HybridCache to SummonerStatsService and wrap stats methods** - `f9cb27c` (feat)
   - Added HybridCache injection and cache key constants
   - Wrapped GetSummonerOverviewAsync, GetChampionStatsAsync, GetRoleBreakdownAsync, GetRecentMatchesAsync, GetMatchDetailAsync
   - Extracted ComputeXxx methods for cache factory functions
   - Defined StatsCacheOptions (5min/2min) and MatchDetailCacheOptions (1hr/15min)

2. **Task 3: Add cache invalidation on summoner refresh** - `670a051` (feat)
   - Injected HybridCache into SummonerRefreshJob
   - Added InvalidateStatsCacheAsync method to clear known cache keys
   - Invalidates common parameter combinations (top 5/10 champions, pages 1-3, overview counts 10/20/50)
   - Called after matches saved to ensure fresh stats on next request

## Files Created/Modified
- `Transcendence.Service.Core/Services/Analysis/Implementations/SummonerStatsService.cs` - All stats methods use HybridCache with GetOrCreateAsync pattern
- `Transcendence.Service.Core/Services/Jobs/SummonerRefreshJob.cs` - Invalidates stats cache after saving new match data

## Decisions Made

**5-minute stats TTL with 2-minute L1 cache**
- Rationale: Stats change when new matches are added (after refresh), shorter than profile/rank cache because stats aggregate from frequently-updated match data

**1-hour match detail TTL with 15-minute L1 cache**
- Rationale: Match details are immutable once stored, can cache longer

**Eager invalidation of known cache keys**
- Rationale: HybridCache doesn't support wildcard invalidation, so we invalidate common parameter combinations (top 5/10 champions, pages 1-3, overview counts 10/20/50)
- Alternative considered: Wait for natural expiration (5min), but eager invalidation provides fresher data immediately after refresh

**Extract compute methods for clean separation**
- Rationale: Keeps cache logic separate from business logic, makes testing easier, follows GetOrCreateAsync pattern cleanly

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

None

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- Profile endpoint now serves cached stats with sub-500ms response times
- Stats automatically refresh when summoner data is updated
- Caching infrastructure established for all future stats endpoints
- Ready for Phase 3 (Static Data Service) to add champion names and rune/item metadata

**No blockers or concerns**

---
*Phase: 02-summoner-profiles*
*Completed: 2026-02-02*
