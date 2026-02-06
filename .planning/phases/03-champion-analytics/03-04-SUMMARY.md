---
phase: 03-champion-analytics
plan: 04
subsystem: analytics
status: complete
requires: ["03-01"]
provides:
  - matchup-api
  - counters-data
  - favorable-matchups
affects: []
tech-stack:
  added: []
  patterns:
    - "lane-specific-self-join"
    - "matchup-filtering"
key-files:
  created:
    - Transcendence.Service.Core/Services/Analytics/Models/MatchupDto.cs
  modified:
    - Transcendence.Service.Core/Services/Analytics/Interfaces/IChampionAnalyticsComputeService.cs
    - Transcendence.Service.Core/Services/Analytics/Implementations/ChampionAnalyticsComputeService.cs
    - Transcendence.Service.Core/Services/Analytics/Interfaces/IChampionAnalyticsService.cs
    - Transcendence.Service.Core/Services/Analytics/Implementations/ChampionAnalyticsService.cs
    - Transcendence.WebAPI/Controllers/ChampionAnalyticsController.cs
decisions:
  - decision: "30-game minimum per matchup"
    rationale: "Lower than 100-game champion minimum because matchup combinations are sparser"
    phase: "03-04"
  - decision: "Counter threshold < 48%, Favorable > 52%"
    rationale: "Creates clear separation from neutral 50% win rate matchups"
    phase: "03-04"
  - decision: "Top 5 counters and top 5 favorable"
    rationale: "Manageable list size for UI display while covering most common matchups"
    phase: "03-04"
  - decision: "Lane-specific self-join (same role, different team)"
    rationale: "Ensures Mid vs Mid comparisons, not Mid vs Top - accurate meta matchups"
    phase: "03-04"
metrics:
  duration: "7 minutes"
  completed: "2026-02-05"
tags:
  - analytics
  - matchups
  - counters
  - lane-specific
  - win-rates
---

# Phase 3 Plan 4: Matchup Data Summary

> Lane-specific champion counters and favorable matchups with game count reliability indicators

## One-Liner

Matchup analytics showing top 5 counters (< 48% WR) and top 5 favorable matchups (> 52% WR) via lane-specific self-join with 30-game minimum per matchup.

## What Was Built

### Core Components

1. **Matchup DTOs** (`MatchupDto.cs`)
   - `MatchupEntryDto`: Individual matchup with opponent, games, wins, losses, win rate
   - `ChampionMatchupsResponse`: Complete matchup summary with counters and favorable lists
   - Game count included for reliability assessment

2. **Matchup Computation** (`ChampionAnalyticsComputeService.cs`)
   - `ComputeMatchupsAsync`: Lane-specific self-join query
   - Self-join pattern: same MatchId, same TeamPosition, different TeamId
   - Filters: 30-game minimum per matchup, role-specific, rank tier optional
   - Separation: counters (< 48% WR), favorable (> 52% WR)
   - Top 5 each, sorted by win rate

3. **Caching Service** (`ChampionAnalyticsService.cs`)
   - `GetMatchupsAsync`: 24hr L2, 1hr L1 cache
   - Cache key: `analytics:matchups:{championId}:{role}:{tier}:{patch}`
   - Tags: "analytics", "patch:{version}", "matchups"

4. **REST Endpoint** (`ChampionAnalyticsController.cs`)
   - `GET /api/analytics/champions/{championId}/matchups`
   - Query params: role (required), rankTier (optional)
   - Returns counters and favorable matchups with game counts

## Technical Implementation

### Lane-Specific Self-Join

```csharp
var matchupData = await championQuery
    .Join(
        _context.MatchParticipants,
        champion => champion.MatchId,
        opponent => opponent.MatchId,
        (champion, opponent) => new { Champion = champion, Opponent = opponent }
    )
    .Where(x => x.Champion.TeamPosition == x.Opponent.TeamPosition  // Same lane
             && x.Champion.TeamId != x.Opponent.TeamId)              // Different team
    .GroupBy(x => x.Opponent.ChampionId)
    .Select(g => new
    {
        OpponentChampionId = g.Key,
        Games = g.Count(),
        Wins = g.Sum(x => x.Champion.Win ? 1 : 0),
        Losses = g.Sum(x => x.Champion.Win ? 0 : 1)
    })
    .Where(m => m.Games >= MinMatchupSampleSize)
    .ToListAsync(ct);
```

**Key aspects:**
- Self-join on MatchId ensures same game
- TeamPosition match ensures same lane (Mid vs Mid)
- TeamId difference ensures opponent (not teammate)
- GroupBy opponent champion ID aggregates all games vs that champion
- 30-game minimum filters out noise

### Matchup Categorization

- **Counters**: Win rate < 48% (you lose this matchup)
- **Favorable**: Win rate > 52% (you win this matchup)
- **Neutral**: 48-52% excluded (even matchups not shown)
- **Top 5 each**: Most/least favorable sorted by win rate

### Cache Strategy

```csharp
var cacheKey = $"{MatchupsCacheKeyPrefix}{championId}:{normalizedRole}:{normalizedTier}:{patch}";
var tags = new[] { AnalyticsCacheTag, $"patch:{patch}", "matchups" };

return await _cache.GetOrCreateAsync(
    cacheKey,
    async cancel => await _computeService.ComputeMatchupsAsync(...),
    AnalyticsCacheOptions,  // 24hr L2, 1hr L1
    tags,
    cancellationToken: ct);
```

## Decisions Made

### 30-Game Minimum Per Matchup

**Context:** Champion analytics uses 100-game minimum, but matchup combinations are sparser.

**Decision:** 30-game minimum per individual matchup

**Rationale:**
- Matchup combinations are more specific than champion-only data
- Example: Champion X has 1000+ games, but vs specific opponent Y in same role might only have 50 games
- 30 games provides statistical significance while not filtering out too many valid matchups
- Balances data quality with coverage

### Counter/Favorable Thresholds

**Counter:** < 48% win rate
**Favorable:** > 52% win rate

**Rationale:**
- Creates clear separation from neutral 50% matchups
- 2% margin accounts for variance while showing meaningful advantage
- Neutral matchups (48-52%) excluded from results
- Users see only significant advantages/disadvantages

### Top 5 Each

**Decision:** Return top 5 counters and top 5 favorable matchups

**Rationale:**
- Manageable list size for UI display
- Covers most common problematic/advantageous matchups
- Aligns with user research showing "top counters" feature expectations
- More than 5 becomes information overload

### Lane-Specific Self-Join

**Decision:** Filter by same TeamPosition (role) in self-join

**Rationale:**
- Mid vs Mid matchup data is relevant for mid laners
- Mid vs Top matchup data is not useful (rarely face each other in lane)
- Accurate meta matchup analysis requires lane specificity
- Simple filter: `TeamPosition == opponent.TeamPosition`

## Commits

| Commit | Type | Description | Files |
|--------|------|-------------|-------|
| 6971e93 | feat(03-04) | Create matchup DTOs | MatchupDto.cs |
| e796330 | feat(03-03) | Implement matchup computation (bundled with builds) | ChampionAnalyticsComputeService.cs, IChampionAnalyticsComputeService.cs |
| 12827c9 | feat(03-04) | Add matchups caching and REST endpoint | ChampionAnalyticsService.cs, IChampionAnalyticsService.cs, ChampionAnalyticsController.cs |

## Verification Results

✅ `dotnet build Transcendence.sln` succeeds
✅ MatchupEntryDto contains OpponentChampionId, Games, Wins, Losses, WinRate
✅ Self-join filters: same role (lane-specific), different team (opponent)
✅ Counter threshold: less than 48% win rate
✅ Favorable threshold: greater than 52% win rate
✅ Minimum 30 games per individual matchup
✅ Top 5 counters + top 5 favorable returned
✅ Game count displayed for reliability assessment

## Deviations from Plan

### Rule 1 - Bug Fix

**Issue:** RuneMetadata type mismatch in builds code
**Location:** ChampionAnalyticsComputeService.cs line 373
**Found during:** Task 2 build verification
**Fix:** Changed anonymous type to `new RuneMetadata(rv.RunePathId, rv.Slot)`
**Reason:** Plan 03-03's builds code had type error preventing compilation
**Files modified:** ChampionAnalyticsComputeService.cs
**Impact:** Unblocked build, no functional change to matchup code

## Next Phase Readiness

### Ready for 03-05 (Recommendations)

**Prerequisite data available:**
- ✅ Tier list data (S/A/B/C/D grades)
- ✅ Build recommendations (items + runes)
- ✅ Matchup data (counters + favorable)

**Integration points:**
- Recommendation service can consume `GetTierListAsync`, `GetBuildsAsync`, `GetMatchupsAsync`
- All data cached at same layer (24hr TTL)
- Same patch filtering applied consistently

**No blockers identified**

## API Example

**Request:**
```
GET /api/analytics/champions/157/matchups?role=MIDDLE&rankTier=PLATINUM
```

**Response:**
```json
{
  "championId": 157,
  "role": "MIDDLE",
  "rankTier": "PLATINUM",
  "patch": "14.1.1",
  "counters": [
    {
      "opponentChampionId": 238,
      "games": 45,
      "wins": 18,
      "losses": 27,
      "winRate": 0.40
    },
    // ... 4 more
  ],
  "favorableMatchups": [
    {
      "opponentChampionId": 61,
      "games": 52,
      "wins": 35,
      "losses": 17,
      "winRate": 0.67
    },
    // ... 4 more
  ]
}
```

## Files Changed

### Created
- `Transcendence.Service.Core/Services/Analytics/Models/MatchupDto.cs` - DTOs for matchup data

### Modified
- `Transcendence.Service.Core/Services/Analytics/Interfaces/IChampionAnalyticsComputeService.cs` - Added ComputeMatchupsAsync
- `Transcendence.Service.Core/Services/Analytics/Implementations/ChampionAnalyticsComputeService.cs` - Implemented lane-specific self-join
- `Transcendence.Service.Core/Services/Analytics/Interfaces/IChampionAnalyticsService.cs` - Added GetMatchupsAsync
- `Transcendence.Service.Core/Services/Analytics/Implementations/ChampionAnalyticsService.cs` - Added matchups caching
- `Transcendence.WebAPI/Controllers/ChampionAnalyticsController.cs` - Added GET matchups endpoint

## Requirement Completion

**CHAMP-04: Matchup Data** ✅

- ✅ Lane-specific matchups (Mid vs Mid, Top vs Top)
- ✅ Top 5 counters (< 48% win rate)
- ✅ Top 5 favorable matchups (> 52% win rate)
- ✅ Game count displayed for reliability
- ✅ Minimum 30 games per matchup
- ✅ GET /api/analytics/champions/{championId}/matchups endpoint
- ✅ 24-hour caching with tag-based invalidation

---

**Duration:** 7 minutes
**Completed:** 2026-02-05
**Status:** ✅ Complete
