---
phase: 01-foundation
plan: 02
subsystem: infra
tags: [caching, hangfire, patch-detection, static-data, ef-core]

# Dependency graph
requires:
  - phase: 01-01
    provides: HybridCache infrastructure and CacheService abstraction
provides:
  - Automatic patch detection running every 6 hours
  - Cache invalidation on patch changes using tag-based removal
  - Patch entity with detection metadata (DetectedAt, IsActive)
  - StaticDataService with cache-aware static data fetching
  - Hangfire recurring job for patch detection
affects: [01-03, 01-04, static-data-consumers]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Scheduled background jobs with Hangfire recurring jobs"
    - "Tag-based cache invalidation for version-dependent data"
    - "Cache-aware data fetching with 30-day L2, 5-min L1 TTL"
    - "Patch versioning with IsActive flag for current patch"

key-files:
  created:
    - Transcendence.Service/Migrations/20260202055038_AddPatchDetectionMetadata.cs
  modified:
    - Transcendence.Data/Models/LoL/Static/Patch.cs
    - Transcendence.Service.Core/Services/StaticData/Implementations/StaticDataService.cs
    - Transcendence.Service.Core/Services/StaticData/Interfaces/IStaticDataService.cs
    - Transcendence.Service.Core/Services/Jobs/UpdateStaticDataJob.cs
    - Transcendence.Service/Workers/DevelopmentWorker.cs
    - Transcendence.Service/Workers/ProductionWorker.cs

key-decisions:
  - "6-hour patch check interval balances detection speed with API efficiency"
  - "30-day cache TTL outlives 2-week patch cycle for historical queries"
  - "IsActive flag simplifies current patch queries"
  - "Immediate job execution in development, schedule-only in production"

patterns-established:
  - "Pattern 1: Tag cache entries with version identifiers for bulk invalidation"
  - "Pattern 2: Separate fetch-and-store methods for cache factory functions"
  - "Pattern 3: Recurring jobs scheduled at startup in worker services"

# Metrics
duration: 3min
completed: 2026-02-02
---

# Phase 01 Plan 02: Automatic Patch Detection Summary

**Scheduled Hangfire job detects LoL patches every 6 hours, invalidates version-tagged cache, and refetches static data automatically**

## Performance

- **Duration:** 3 min
- **Started:** 2026-02-02T05:50:08Z
- **Completed:** 2026-02-02T05:53:01Z
- **Tasks:** 3
- **Files modified:** 8

## Accomplishments
- Patch entity tracks detection time and active status with EF Core migration
- StaticDataService detects new patches and invalidates cache by tag
- Hangfire recurring job runs every 6 hours, immediately on dev startup
- Static data (runes, items) uses cache with 30-day L2, 5-min L1 TTL
- Tag-based cache invalidation ensures fresh data after patch changes

## Task Commits

Each task was committed atomically:

1. **Task 1: Add patch detection metadata to Patch entity** - `87053b8` (feat)
   - Added DetectedAt and IsActive fields to Patch model
   - Created EF Core migration for new columns

2. **Task 2: Enhance StaticDataService with patch detection and cache integration** - `c035d1c` (feat)
   - Added ICacheService dependency to StaticDataService
   - Implemented DetectAndRefreshAsync with cache invalidation
   - Extracted FetchAndStoreRunesAsync and FetchAndStoreItemsAsync methods
   - Configured patch-versioned cache keys with tags

3. **Task 3: Schedule UpdateStaticDataJob with Hangfire recurring job** - `c93c6f5` (feat)
   - Updated UpdateStaticDataJob to call DetectAndRefreshAsync
   - Scheduled recurring job with 6-hour cron schedule (0 */6 * * *)
   - Added immediate execution in DevelopmentWorker

## Files Created/Modified
- `Transcendence.Data/Models/LoL/Static/Patch.cs` - Added DetectedAt and IsActive fields for patch tracking
- `Transcendence.Service/Migrations/20260202055038_AddPatchDetectionMetadata.cs` - EF Core migration for Patch entity changes
- `Transcendence.Service.Core/Services/StaticData/Implementations/StaticDataService.cs` - DetectAndRefreshAsync method with cache invalidation
- `Transcendence.Service.Core/Services/StaticData/Interfaces/IStaticDataService.cs` - Added DetectAndRefreshAsync to interface
- `Transcendence.Service.Core/Services/Jobs/UpdateStaticDataJob.cs` - Calls DetectAndRefreshAsync instead of UpdateStaticDataAsync
- `Transcendence.Service/Workers/DevelopmentWorker.cs` - Schedules recurring job and runs immediately on startup
- `Transcendence.Service/Workers/ProductionWorker.cs` - Schedules recurring job only (no immediate execution)

## Decisions Made

1. **6-hour check interval**: Satisfies requirement ("auto-updates within 6 hours") while minimizing unnecessary API calls. Patch cycle is ~2 weeks, so hourly checks would waste 95% of requests.

2. **30-day cache TTL**: Outlives 2-week patch cycle. Old patch data persists in cache for historical queries if needed. Natural expiration handled by patch-versioned keys.

3. **IsActive flag over timestamp queries**: Simplifies "get current patch" to `FirstOrDefault(p => p.IsActive)` instead of ordering by ReleaseDate. Single source of truth for active patch.

4. **Immediate execution in development only**: Enables testing patch detection without waiting 6 hours. Production avoids API waste on every deployment restart.

5. **Tag-based invalidation**: Cache entries tagged with `patch-{version}` allow bulk invalidation on patch change. Old patch keys remain but are unused—new patch gets fresh keys.

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

None - EF Core migration generated correctly, StaticDataService compiled with ICacheService dependency, Hangfire scheduling worked as expected.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

**Ready for plan 01-03 (Rate Limiting):**
- Patch detection infrastructure complete
- Cache invalidation mechanism established
- Static data refresh automated

**No blockers or concerns.**

---
*Phase: 01-foundation*
*Completed: 2026-02-02*
