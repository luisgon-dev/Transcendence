---
phase: 01-foundation
plan: 03
subsystem: api
tags: [hangfire, retry-logic, exponential-backoff, entity-framework, riot-api]

# Dependency graph
requires:
  - phase: 01-01
    provides: HybridCache infrastructure for caching
provides:
  - FetchStatus enum for tracking match fetch state (Unfetched, Success, TemporaryFailure, PermanentlyUnfetchable, OutsideRetentionWindow)
  - Exponential backoff retry logic (30s, 60s, 120s, 300s) with max 5 attempts
  - Retention window validation (2 years) to prevent wasted API calls
  - RetryFailedMatchesJob for periodic cleanup of missed retries
affects: [01-04, match-processing, data-retention]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Exponential backoff retry pattern with Hangfire scheduling"
    - "FetchStatus state machine for tracking entity fetch lifecycle"
    - "Global EF Core query filters to exclude unfetchable entities"

key-files:
  created:
    - Transcendence.Service/Migrations/20260202055204_AddMatchFetchStatus.cs
    - Transcendence.Service.Core/Services/Jobs/RetryFailedMatchesJob.cs
  modified:
    - Transcendence.Data/Models/LoL/Match/Match.cs
    - Transcendence.Data/TranscendenceContext.cs
    - Transcendence.Service.Core/Services/RiotApi/Implementations/MatchService.cs
    - Transcendence.Service.Core/Services/RiotApi/Interfaces/IMatchService.cs
    - Transcendence.Service/Workers/ProductionWorker.cs

key-decisions:
  - "Exponential backoff delays: 30s, 60s, 120s, 300s (4 attempts before permanent failure)"
  - "Max 5 retry attempts before marking PermanentlyUnfetchable"
  - "2-year retention window check before API calls (Riot API retention policy)"
  - "Global query filter excludes PermanentlyUnfetchable matches from normal queries"
  - "Trust Camille SDK rate limiting (no custom throttling)"
  - "RetryFailedMatchesJob runs hourly as safety net (not primary retry mechanism)"
  - "10-minute cutoff for retry job to allow exponential backoff to work"

patterns-established:
  - "FetchStatus state machine: Unfetched → TemporaryFailure → Success/PermanentlyUnfetchable/OutsideRetentionWindow"
  - "Retry metadata tracking: RetryCount, FetchedAt, LastAttemptAt, LastErrorMessage"
  - "Hangfire BackgroundJob.Schedule for deferred retry execution"

# Metrics
duration: 3min 30sec
completed: 2026-02-02
---

# Phase 01 Plan 03: Safe Retry Logic Summary

**Match fetch retry with exponential backoff (30s-300s), retention window validation (2 years), and state tracking to prevent infinite loops**

## Performance

- **Duration:** 3min 30sec
- **Started:** 2026-02-02T05:51:14Z
- **Completed:** 2026-02-02T05:54:44Z
- **Tasks:** 3
- **Files modified:** 10

## Accomplishments
- FetchStatus enum and retry metadata added to Match entity with EF Core migration
- Exponential backoff retry logic with 30s, 60s, 120s, 300s delays
- Retention window validation prevents fetching matches >2 years old
- RetryFailedMatchesJob scheduled hourly to catch missed retries
- Global query filter excludes PermanentlyUnfetchable matches from normal queries

## Task Commits

Each task was committed atomically:

1. **Task 1: Add FetchStatus tracking to Match entity** - `01fbdda` (feat)
   - Added FetchStatus enum (Unfetched, Success, TemporaryFailure, PermanentlyUnfetchable, OutsideRetentionWindow)
   - Added retry metadata fields (RetryCount, FetchedAt, LastAttemptAt, LastErrorMessage)
   - Added global query filter to exclude PermanentlyUnfetchable matches
   - Created EF Core migration with all new columns

2. **Task 2: Implement retry logic with retention window checks** - `bbc6bf5` (feat)
   - Added FetchMatchWithRetryAsync method to IMatchService interface
   - Implemented exponential backoff retry logic (30s, 60s, 120s, 300s)
   - Added retention window validation (2 years) before API calls
   - Schedule retries with Hangfire BackgroundJob
   - Mark matches as PermanentlyUnfetchable after 5 failed attempts

3. **Task 3: Create RetryFailedMatchesJob for periodic retry cleanup** - `ccb3319` (feat)
   - Created RetryFailedMatchesJob to retry TemporaryFailure matches
   - Query matches with LastAttemptAt > 10 minutes ago
   - Batch size of 100 to prevent API rate limit exhaustion
   - Scheduled job hourly in ProductionWorker as safety net

## Files Created/Modified

- `Transcendence.Data/Models/LoL/Match/Match.cs` - Added FetchStatus enum and retry tracking fields
- `Transcendence.Data/TranscendenceContext.cs` - Added global query filter for PermanentlyUnfetchable matches
- `Transcendence.Service/Migrations/20260202055204_AddMatchFetchStatus.cs` - Migration for new Match columns
- `Transcendence.Service.Core/Services/RiotApi/Interfaces/IMatchService.cs` - Added FetchMatchWithRetryAsync method
- `Transcendence.Service.Core/Services/RiotApi/Implementations/MatchService.cs` - Implemented retry logic with retention window checks
- `Transcendence.Service.Core/Services/Jobs/RetryFailedMatchesJob.cs` - Periodic job to retry failed matches
- `Transcendence.Service/Workers/ProductionWorker.cs` - Scheduled RetryFailedMatchesJob hourly

## Decisions Made

1. **Exponential backoff schedule (30s, 60s, 120s, 300s):** Balances responsiveness with API courtesy. First retry is quick (30s) for transient failures, but backs off if issue persists. Total retry window ~10 minutes before permanent failure.

2. **Max 5 attempts before PermanentlyUnfetchable:** Prevents infinite retry loops while still being persistent. After 5 failures over ~10 minutes, data is likely permanently unavailable (404, deleted match, etc.).

3. **2-year retention window check:** Riot API only retains match data for 2 years. Checking retention window BEFORE attempting fetch prevents wasted API calls on data that doesn't exist anymore.

4. **Global query filter for PermanentlyUnfetchable:** Unfetchable matches are historical records (for metrics/reporting) but shouldn't appear in normal queries. Use `IgnoreQueryFilters()` when you need to access them for admin purposes.

5. **Trust Camille SDK rate limiting:** Camille already parses X-Rate-Limit-* headers and respects Retry-After. Adding custom throttling would create double-throttling and unnecessarily delay requests.

6. **RetryFailedMatchesJob as safety net:** Hourly job catches edge cases (service restart, missed scheduled retries). Primary retry mechanism is per-match exponential backoff via Hangfire BackgroundJob.Schedule.

7. **10-minute cutoff for retry job:** Prevents immediate re-attempts when the hourly job runs. Gives exponential backoff time to work. Only retries matches that fell through the cracks.

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

None - all implementations worked as expected on first attempt.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

Match retry infrastructure is complete and ready for use. Key capabilities delivered:

**Ready for next phase:**
- Match fetch failures now retry automatically with exponential backoff
- Retention window validation prevents wasted API calls on old data
- PermanentlyUnfetchable state prevents infinite retry loops
- Hourly cleanup job provides safety net for missed retries

**No blockers.**

**Recommendations for next phase:**
- Plan 01-04 (match processing) should use `FetchMatchWithRetryAsync` instead of direct `GetMatchDetailsAsync` to get automatic retry behavior
- Any code that needs to query all matches (including unfetchable) should use `.IgnoreQueryFilters()` to bypass the global filter
- Consider adding metrics/logging to track retry success rates and identify patterns in permanent failures

---
*Phase: 01-foundation*
*Completed: 2026-02-02*
