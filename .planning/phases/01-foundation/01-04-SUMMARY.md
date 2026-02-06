---
phase: 01-foundation
plan: 04
subsystem: api
tags: [data-freshness, timestamps, dto, api-response, metadata]

# Dependency graph
requires:
  - phase: 01-03
    provides: Match retry infrastructure with FetchedAt timestamps
provides:
  - DataAgeMetadata DTO with FetchedAt, Age, and AgeDescription properties
  - SummonerProfileResponse DTO with ProfileAge and RankAge metadata
  - MatchHistoryResponse DTO with DataAge per match summary
  - UpdatedAt timestamps on Summoner and Rank entities
  - API responses include human-readable data freshness information
affects: [02-profile-enrichment, api-clients, desktop-app]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "DataAgeMetadata pattern for timestamp metadata in API responses"
    - "Separate age tracking for different data types (ProfileAge vs RankAge)"
    - "Human-friendly age descriptions (Just now, X minutes ago, etc.)"

key-files:
  created:
    - Transcendence.Service.Core/Services/RiotApi/DTOs/DataAgeMetadata.cs
    - Transcendence.Service.Core/Services/RiotApi/DTOs/SummonerProfileResponse.cs
    - Transcendence.Service.Core/Services/RiotApi/DTOs/MatchHistoryResponse.cs
    - Transcendence.Service/Migrations/20260202055903_AddDataFreshnessTimestamps.cs
  modified:
    - Transcendence.Data/Models/LoL/Account/Rank.cs
    - Transcendence.Data/Models/LoL/Account/Summoner.cs
    - Transcendence.WebAPI/Controllers/SummonersController.cs

key-decisions:
  - "Separate ProfileAge and RankAge metadata: Profile data (name, level) changes rarely, rank data changes frequently - different freshness expectations"
  - "Human-friendly age descriptions: Just now (<5 min), X minutes ago (<1 hr), X hours ago (<1 day), X days ago (>1 day)"
  - "UpdatedAt on Summoner entity: Added for ProfileAge tracking (was missing but required by plan)"
  - "MatchParticipant inherits timestamp: Uses Match.FetchedAt via relationship - no separate timestamp needed"
  - "API contract change: SummonersController now returns SummonerProfileResponse DTO instead of raw Summoner entity"

patterns-established:
  - "DataAgeMetadata reusable component: Single DTO for timestamp metadata across all API responses"
  - "Age calculation via computed properties: Age and AgeDescription are readonly properties, not serialized fields"
  - "Fallback to UtcNow: If timestamp missing, use current time to prevent null reference errors"

# Metrics
duration: 4min
completed: 2026-02-02
---

# Phase 01 Plan 04: Data Freshness Metadata Summary

**API responses include data age metadata with human-readable descriptions (Just now, X minutes ago) for client transparency**

## Performance

- **Duration:** 4min
- **Started:** 2026-02-02T05:58:04Z
- **Completed:** 2026-02-02T06:02:01Z
- **Tasks:** 3
- **Files modified:** 8

## Accomplishments

- Added UpdatedAt timestamps to Summoner and Rank entities with EF Core migration
- Created DataAgeMetadata DTO with FetchedAt, Age, and AgeDescription properties
- Updated SummonerController to return SummonerProfileResponse with ProfileAge and RankAge metadata
- API responses now show data freshness in human-readable format (Just now, X minutes ago, etc.)

## Task Commits

Each task was committed atomically:

1. **Task 1: Add timestamp metadata to data entities** - `5ddc6a4` (feat)
   - Added UpdatedAt timestamp to Rank entity
   - Added UpdatedAt timestamp to Summoner entity (deviation)
   - Created EF Core migration AddDataFreshnessTimestamps
   - Applied migration to database

2. **Task 2: Create response DTOs with data age metadata** - `32c9164` (feat)
   - Created DataAgeMetadata.cs with FetchedAt, Age, AgeDescription
   - Created SummonerProfileResponse.cs with ProfileAge and RankAge
   - Created MatchHistoryResponse.cs with DataAge per match

3. **Task 3: Update API controllers to populate data age metadata** - `c2abda6` (feat)
   - Updated SummonersController GetByRiotId endpoint
   - Map Summoner and Rank entities to SummonerProfileResponse DTO
   - Populate ProfileAge from Summoner.UpdatedAt
   - Populate RankAge from Rank.UpdatedAt (solo or flex)

## Files Created/Modified

- `Transcendence.Data/Models/LoL/Account/Rank.cs` - Added UpdatedAt timestamp for rank data freshness
- `Transcendence.Data/Models/LoL/Account/Summoner.cs` - Added UpdatedAt timestamp for profile data freshness
- `Transcendence.Service/Migrations/20260202055903_AddDataFreshnessTimestamps.cs` - Migration adding UpdatedAt columns to Summoners and Ranks tables
- `Transcendence.Service.Core/Services/RiotApi/DTOs/DataAgeMetadata.cs` - Reusable DTO for timestamp metadata with Age and AgeDescription
- `Transcendence.Service.Core/Services/RiotApi/DTOs/SummonerProfileResponse.cs` - Profile response DTO with ProfileAge and RankAge metadata
- `Transcendence.Service.Core/Services/RiotApi/DTOs/MatchHistoryResponse.cs` - Match history response DTO with DataAge per match
- `Transcendence.WebAPI/Controllers/SummonersController.cs` - Updated to return SummonerProfileResponse with data age metadata

## Decisions Made

1. **Separate ProfileAge and RankAge metadata:** Profile data (name, level) changes rarely. Rank data changes frequently. Different freshness expectations mean clients may want to refresh rank more often than profile.

2. **Human-friendly age descriptions:** DataAgeMetadata.AgeDescription provides "Just now" (<5 min), "X minutes ago" (<1 hr), "X hours ago" (<1 day), "X days ago" (>1 day). Clients can use raw Age property for logic, AgeDescription for display.

3. **UpdatedAt on Summoner entity:** Plan assumed Summoner.UpdatedAt existed for ProfileAge mapping in Task 3, but it didn't. Added to Summoner entity (Rule 2 deviation - missing critical functionality).

4. **MatchParticipant inherits timestamp:** Match entity already has FetchedAt from Plan 01-03. MatchParticipant accesses timestamp via Match relationship - no separate timestamp needed.

5. **API contract change:** SummonersController now returns SummonerProfileResponse DTO instead of raw Summoner entity. This is acceptable because Phase 1 is Foundation work - no existing clients to break. Desktop app will be built against this contract.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 2 - Missing Critical] Added Summoner.UpdatedAt timestamp**
- **Found during:** Task 1 (Add timestamp metadata to data entities)
- **Issue:** Plan Task 3 assumed `summoner.UpdatedAt` exists for ProfileAge mapping, but Summoner entity didn't have UpdatedAt field
- **Fix:** Added UpdatedAt field to Summoner entity with DateTime.UtcNow default, included in AddDataFreshnessTimestamps migration
- **Files modified:** Transcendence.Data/Models/LoL/Account/Summoner.cs, Transcendence.Service/Migrations/20260202055903_AddDataFreshnessTimestamps.cs
- **Verification:** Migration adds UpdatedAt column to Summoners table, build succeeds
- **Committed in:** 5ddc6a4 (Task 1 commit)

---

**Total deviations:** 1 auto-fixed (1 missing critical)
**Impact on plan:** Auto-fix necessary for ProfileAge tracking. Plan assumed this field existed but it didn't. No scope creep - required for plan completion.

## Issues Encountered

None - all implementations worked as expected on first attempt.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

Data freshness metadata infrastructure is complete and ready for use. Key capabilities delivered:

**Ready for next phase:**
- API responses include FetchedAt timestamps and human-readable age descriptions
- Clients can display data freshness in UI (e.g., "Rank updated 2 minutes ago")
- Separate ProfileAge and RankAge allow different refresh strategies
- DataAgeMetadata pattern established for future response DTOs

**No blockers.**

**Recommendations for next phase:**
- Phase 2 (profile enrichment) should use DataAgeMetadata pattern for match history responses
- Consider adding "Refresh" button in client UI when data age exceeds threshold (e.g., rank >5 min old)
- Frontend should poll /refresh endpoint when data is stale, then poll GET endpoint until data updates

---
*Phase: 01-foundation*
*Completed: 2026-02-02*
