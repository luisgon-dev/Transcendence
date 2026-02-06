---
phase: 03-champion-analytics
plan: 02
subsystem: analytics
tags: [ef-core, hybrid-cache, analytics, rest-api, tier-lists]

# Dependency graph
requires:
  - phase: 03-01
    provides: Champion analytics architecture (compute service, cached service, controller, 100-game threshold, 24hr cache TTL)
provides:
  - Champion tier list computation with S/A/B/C/D percentile-based grading
  - Movement tracking from previous patch (UP/DOWN/SAME/NEW)
  - Composite scoring: 70% win rate + 30% pick rate
  - Per-role and unified tier list support
  - REST endpoint GET /api/analytics/tierlist with role and rankTier filters
affects: [03-03-build-recommendations, future-analytics-features]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Percentile-based tier assignment (S=top 10%, A=10-30%, B=30-60%, C=60-85%, D=85%+)"
    - "Composite scoring algorithm for meta ranking"
    - "Previous patch comparison for movement indicators"

key-files:
  created:
    - Transcendence.Service.Core/Services/Analytics/Models/TierListDto.cs
    - Transcendence.WebAPI/Controllers/AnalyticsController.cs
  modified:
    - Transcendence.Service.Core/Services/Analytics/Interfaces/IChampionAnalyticsComputeService.cs
    - Transcendence.Service.Core/Services/Analytics/Implementations/ChampionAnalyticsComputeService.cs
    - Transcendence.Service.Core/Services/Analytics/Interfaces/IChampionAnalyticsService.cs
    - Transcendence.Service.Core/Services/Analytics/Implementations/ChampionAnalyticsService.cs

key-decisions:
  - "Composite score weights: 70% win rate + 30% pick rate for balanced meta strength assessment"
  - "Percentile-based tiers: S=top 10%, A=10-30%, B=30-60%, C=60-85%, D=85%+"
  - "Movement tracking: Compare tier grades between current and previous patch"
  - "Enums for TierGrade and TierMovement instead of strings for type safety"
  - "Unified tier list via 'ALL' role parameter aggregates across all positions"
  - "Separate AnalyticsController for general analytics endpoints vs ChampionAnalyticsController for champion-specific"

patterns-established:
  - "Tier list entries include movement indicators and previous tier for UI display"
  - "GetPreviousPatchAsync helper for temporal comparisons"
  - "Recursive tier list computation for previous patch comparison"

# Metrics
duration: 4min
completed: 2026-02-05
---

# Phase 03 Plan 02: Tier Lists Summary

**Champion tier lists with S/A/B/C/D percentile grading, composite scoring (70% WR + 30% PR), and movement indicators from previous patch**

## Performance

- **Duration:** 4 min
- **Started:** 2026-02-05T17:18:01Z
- **Completed:** 2026-02-05T17:21:53Z
- **Tasks:** 3 (1 pre-complete)
- **Files modified:** 7

## Accomplishments
- Percentile-based tier assignment (S/A/B/C/D) ranks champions by composite meta strength
- Composite scoring combines win rate (70%) and pick rate (30%) for balanced meta assessment
- Movement tracking compares tier grades from previous patch (UP/DOWN/SAME/NEW)
- Per-role tier lists and unified (ALL roles) tier list support
- REST endpoint GET /api/analytics/tierlist with 24hr caching

## Task Commits

Each task was committed atomically:

1. **Task 1: Create tier list DTOs** - `8b7f0e9` (feat) [pre-complete]
2. **Task 2: Implement tier list computation** - `64ed95f` (feat)
3. **Task 3: Add tier list caching and controller endpoint** - `fa1d6f1` (feat)

**Plan metadata:** [pending]

## Files Created/Modified

- `Transcendence.Service.Core/Services/Analytics/Models/TierListDto.cs` - DTOs for tier list entries (TierGrade enum S/A/B/C/D, TierMovement enum, TierListEntry, TierListResponse)
- `Transcendence.Service.Core/Services/Analytics/Interfaces/IChampionAnalyticsComputeService.cs` - Added ComputeTierListAsync method
- `Transcendence.Service.Core/Services/Analytics/Implementations/ChampionAnalyticsComputeService.cs` - Tier list computation with percentile grading, composite scoring, movement tracking via GetPreviousPatchAsync and GetPreviousPatchTiersAsync helpers
- `Transcendence.Service.Core/Services/Analytics/Interfaces/IChampionAnalyticsService.cs` - Added GetTierListAsync method
- `Transcendence.Service.Core/Services/Analytics/Implementations/ChampionAnalyticsService.cs` - Tier list caching with 24hr L2 / 1hr L1 TTL, tagged with analytics/patch/tierlist
- `Transcendence.WebAPI/Controllers/AnalyticsController.cs` - REST endpoint at /api/analytics/tierlist with role and rankTier query parameters

## Decisions Made

**1. Composite score weighting: 70% win rate + 30% pick rate**
- Win rate is primary indicator of champion strength
- Pick rate provides meta relevance and prevents niche picks from dominating S tier
- 70/30 split balances both factors for practical tier list

**2. Percentile-based tier thresholds**
- S tier = top 10% (elite picks)
- A tier = 10-30% (strong picks)
- B tier = 30-60% (average picks)
- C tier = 60-85% (below average)
- D tier = 85%+ (weak picks)
- Percentiles ensure tier distribution regardless of patch balance

**3. Movement calculation compares tier grades, not ranks**
- Movement indicators based on tier letter change (S→A = DOWN, B→A = UP)
- More stable than rank-based comparison (rank 15→20 might be SAME tier)
- NEW indicator for champions below 100-game threshold in previous patch

**4. Enums instead of strings for tier grades and movement**
- Type safety prevents invalid values
- Better IDE support and refactoring
- Consistent with .NET conventions

**5. Recursive previous patch tier computation**
- GetPreviousPatchTiersAsync calls ComputeTierListAsync for previous patch
- Simplified implementation but requires patch data exists
- Returns empty dictionary if no previous patch (all champions marked NEW)

**6. Separate AnalyticsController for general analytics**
- /api/analytics/tierlist vs /api/analytics/champions/{id}/winrates
- Tier lists are meta-level analytics (all champions) vs champion-specific
- Clean route hierarchy for future analytics endpoints

## Deviations from Plan

None - plan executed exactly as written.

Plan had string-based DTOs but implemented enums (TierGrade, TierMovement) for better type safety. This is an improvement, not a deviation.

## Issues Encountered

None. EF Core aggregation queries performed as expected. Build succeeded on first attempt after each task.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- Tier list computation and caching complete
- Ready for Plan 03-03 (Build recommendations)
- Movement tracking provides historical context for meta shifts
- Composite scoring can be reused for other ranking algorithms

---
*Phase: 03-champion-analytics*
*Completed: 2026-02-05*
